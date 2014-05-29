using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.Threading;

namespace LuaInterface
{
    /// <summary>
    /// Main class of CCLib.LuaInterface Object-oriented wrapper to Lua API
    /// </summary>
    public class Lua : IDisposable
    {
        readonly static string InitLuaNet =
        @"local metatable = {}
        local rawget = rawget
        local import_type = luanet.import_type
        local load_assembly = luanet.load_assembly
        luanet.error, luanet.type = error, type
        -- Lookup a .NET identifier component.
        function metatable:__index(key) -- key is e.g. 'Form'
            -- Get the fully-qualified name, e.g. 'System.Windows.Forms.Form'
            local fqn = rawget(self,'.fqn')
            fqn = ((fqn and fqn .. '.') or '') .. key

            -- Try to find either a luanet function or a CLR type
            local obj = rawget(luanet,key) or import_type(fqn)

            -- If key is neither a luanet function or a CLR type, then it is simply
            -- an identifier component.
            if obj == nil then
                -- It might be an assembly, so we load it too.
                pcall(load_assembly,fqn)
                obj = { ['.fqn'] = fqn }
                setmetatable(obj, metatable)
            end

            -- Cache this lookup
            rawset(self, key, obj)
            return obj
        end

        -- A non-type has been called; e.g. foo = System.Foo()
        function metatable:__call(...)
            error('No such type: ' .. rawget(self,'.fqn'), 2)
        end

        -- This is the root of the .NET namespace
        luanet['.fqn'] = false
        setmetatable(luanet, metatable)

        -- Preload the mscorlib assembly
        luanet.load_assembly('mscorlib')";

        public KopiLua.LuaState LuaState
        {
            get;
            protected set;
        }
        public ObjectTranslator Translator
        {
            get;
            protected set;
        }


        KopiLua.LuaNativeFunction _PanicCallback;
        KopiLua.LuaNativeFunction TracebackFunction;

        public Lua()
        {
            LuaState = LuaCore.LuaLNewState();
            LuaCore.LuaLOpenLibs(LuaState);
            
            //Show that CCLib is loaded
            SetLoaded();
            
            Translator = new ObjectTranslator(this, LuaState);
            
            TracebackFunction = new KopiLua.LuaNativeFunction(Traceback);

            // We need to keep this in a managed reference so the delegate doesn't get garbage collected
            _PanicCallback = new KopiLua.LuaNativeFunction(PanicCallback);
            LuaCore.LuaAtPanic(LuaState, _PanicCallback);

        }

        private bool _StatePassed;

        /// <summary>
        /// CAUTION: CCLib.LuaInterface.Lua instances can't share the same lua state!
        /// </summary>
        public Lua(KopiLua.LuaState LState)
        {
            LuaCore.LuaPushString(LState, "CCLib.LuaInterface LOADED");
            LuaCore.LuaGetTable(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX);

            if (LuaCore.LuaToBoolean(LState, -1))
            {
                LuaCore.LuaSetTop(LState, -2);
                throw new LuaException("There is already a CCLib.LuaInterface.Lua instance associated with this Lua state");
            }
            else
            {
                LuaState = LState;

                //Show that CCLib is loaded
                SetLoaded();               
                
                Translator = new ObjectTranslator(this, this.LuaState);
            }

            _StatePassed = true;
        }
        #region Loading

        /// <summary>
        /// Sets that CCLib.LuaInterface is loaded to prevent duplication
        /// </summary>
        private void SetLoaded()
        {
            LuaCore.LuaPushString(LuaState, "CCLib.LuaInterface LOADED");
            LuaCore.LuaPushBoolean(LuaState, true);
            LuaCore.LuaSetTable(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX);
            LuaCore.LuaNewTable(LuaState);
        }
        public void LoadLuaNet()
        {
            LuaCore.LuaSetGlobal(LuaState, "luanet");
            LuaCore.LuaPushValue(LuaState, (int)LuaIndexes.LUA_GLOBALSINDEX);
            LuaCore.LuaGetGlobal(LuaState, "luanet");
            LuaCore.LuaPushString(LuaState, "getmetatable");
            LuaCore.LuaGetGlobal(LuaState, "getmetatable");
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaReplace(LuaState, (int)LuaIndexes.LUA_GLOBALSINDEX);

            LuaCore.LuaReplace(LuaState, (int)LuaIndexes.LUA_GLOBALSINDEX);
            LuaCore.LuaLDoString(LuaState, Lua.InitLuaNet);

            Translator.SetGlobalFunctions(LuaState);
        }
        #endregion

        public void Close()
        {
            if (_StatePassed)
                return;

            if (LuaState != null)
            {
                LuaCore.LuaClose(LuaState);
            }
            //LuaState = IntPtr.Zero; <- suggested by Christopher Cebulski http://luaforge.net/forum/forum.php?thread_id=44593&forum_id=146
        }

        /// <summary>
        /// Called for each lua_lock call 
        /// </summary>
        /// <param name="LuaState"></param>
        /// Not yet used
        int LockCallback(KopiLua.LuaState LuaState)
        {
            return 0;
        }

        /// <summary>
        /// Called for each lua_unlock call 
        /// </summary>
        /// <param name="LuaState"></param>
        /// Not yet used
        int UnlockCallback(KopiLua.LuaState LuaState)
        {
            return 0;
        }

        static int PanicCallback(KopiLua.LuaState LuaState)
        {
            throw new LuaException(String.Format("unprotected error in call to Lua API ({0})", LuaCore.LuaToString(LuaState, -1)));
        }

        /// <summary>
        /// Assuming we have a Lua error string sitting on the stack, throw a C# exception out to the user's app
        /// </summary>
        /// <exception cref="LuaScriptException">Thrown if the script caused an exception</exception>
        void ThrowExceptionFromError(int OldTop)
        {
            object Err = Translator.GetObject(LuaState, -1);
            LuaCore.LuaSetTop(LuaState, OldTop);

            // A pre-wrapped exception - just rethrow it (stack trace of InnerException will be preserved)
            LuaException LuaEx = Err as LuaException;
            if (LuaEx != null) throw LuaEx;

            // A non-wrapped Lua error (best interpreted as a string) - wrap it and throw it
            if (Err == null) Err = "Unknown Lua Error";
            throw new LuaException(Err.ToString());
        }

        /// <summary>
        /// Convert C# exceptions into Lua errors
        /// </summary>
        /// <returns>num of things on stack</returns>
        /// <param name="e">null for no pending exception</param>
        internal int SetPendingException(Exception e)
        {
            Exception CaughtExcept = e;

            if (CaughtExcept != null)
            {
                Translator.ThrowError(LuaState, CaughtExcept);
                LuaCore.LuaPushNil(LuaState);

                return 1;
            }

            return 0;
        }
        #region Execution
        private bool Executing;

        /// <summary>
        /// True while a script is being executed
        /// </summary>
        public bool IsExecuting { get { return Executing; } }

        /// <summary>
        /// Load a string into the Lua stack
        /// </summary>
        /// <param name="Chunk"></param>
        /// <param name="Name"></param>
        /// <returns></returns>
        public LuaFunction LoadString(string Chunk, string Name)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);

            Executing = true;
            try
            {
                if (LuaCore.LuaLLoadBuffer(LuaState, Chunk, Chunk.Length, Name) != 0)
                {
                    ThrowExceptionFromError(OldTop);
                }
            }
            finally { Executing = false; }

            LuaFunction Result = Translator.getFunction(LuaState, -1);
            Translator.PopValues(LuaState, OldTop);

            return Result;
        }

        /// <summary>
        /// Load a file into the Lua Stack
        /// </summary>
        /// <param name="FileName"></param>
        /// <returns></returns>
        public LuaFunction LoadFile(string FileName)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            if (LuaCore.LuaLLoadFile(LuaState, FileName) != 0)
            {
                ThrowExceptionFromError(OldTop);
            }

            LuaFunction Result = Translator.getFunction(LuaState, -1);
            Translator.PopValues(LuaState, OldTop);

            return Result;
        }

        /// <summary>
        /// Excutes a Lua chunk.
        /// </summary>
        /// <param name="Chunk">Chunk to execute</param>
        /// <param name="ChunkName">Name to associate with the chunk</param>
        /// <returns>The chunk's return values as an array</returns>
        public object[] DoString(string Chunk, string ChunkName = "chunk")
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            Executing = true;
            if (LuaCore.LuaLLoadBuffer(LuaState, Chunk, Chunk.Length, ChunkName) == 0)
            {
                try
                {
                    int Res = LuaCore.LuaPCall(LuaState, 0, -1, 0);
                    if (Res == 0)
                    {
                        return Translator.PopValues(LuaState, OldTop);
                    }
                    else
                    {
                        ThrowExceptionFromError(OldTop);
                    }
                }
                finally { Executing = false; }
            }
            else
            {
                ThrowExceptionFromError(OldTop);
            }
            return null;
        }

        private int Traceback(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaGetGlobal(LuaState, "debug");
            LuaCore.LuaGetField(LuaState, -1, "traceback");
            LuaCore.LuaPushValue(LuaState, 1);
            LuaCore.LuaPushNumber(LuaState, 2);
            LuaCore.LuaCall(LuaState, 2, 1);
            return 1;
        }

        /// <summary>
        /// Excutes a Lua chunk.
        /// </summary>
        /// <param name="FileName">Name of file</param>
        /// <returns>The chunk's return values as an array</returns>
        public object[] DoFile(string FileName)
        {
            LuaCore.LuaPushStdCallCFunction(LuaState, TracebackFunction);
            int OldTop = LuaCore.LuaGetTop(LuaState);
            if (LuaCore.LuaLLoadFile(LuaState, FileName) == 0)
            {
                Executing = true;
                try
                {
                    if (LuaCore.LuaPCall(LuaState, 0, -1, -2) == 0)
                    {
                        return Translator.PopValues(LuaState, OldTop);
                    }
                    else
                    {
                        ThrowExceptionFromError(OldTop);
                    }
                }
                finally { Executing = false; }
            }
            else
            {
                ThrowExceptionFromError(OldTop);
            }

            return null;
        }
        #endregion
        /// <summary>
        /// Indexer for global variables from the LuaInterpreter 
        /// Supports navigation of tables by using . operator
        /// </summary>
        public object this[string FullPath]
        {
            get
            {
                object ReturnValue = null;
                int OldTop = LuaCore.LuaGetTop(LuaState);
                string[] Path = FullPath.Split(new char[] { '.' });
                LuaCore.LuaGetGlobal(LuaState, Path[0]);
                ReturnValue = Translator.GetObject(LuaState, -1);
                if (Path.Length > 1)
                {
                    string[] RemainingPath = new string[Path.Length - 1];
                    Array.Copy(Path, 1, RemainingPath, 0, Path.Length - 1);
                    ReturnValue = GetObject(RemainingPath);
                }
                LuaCore.LuaSetTop(LuaState, OldTop);
                return ReturnValue;
            }
            set
            {
                int OldTop = LuaCore.LuaGetTop(LuaState);
                string[] Path = FullPath.Split(new char[] { '.' });
                if (Path.Length == 1)
                {
                    Translator.Push(LuaState, value);
                    LuaCore.LuaSetGlobal(LuaState, FullPath);
                }
                else
                {
                    LuaCore.LuaGetGlobal(LuaState, Path[0]);
                    string[] RemainingPath = new string[Path.Length - 1];
                    Array.Copy(Path, 1, RemainingPath, 0, Path.Length - 1);
                    SetObject(RemainingPath, value);
                }
                LuaCore.LuaSetTop(LuaState, OldTop);
            }
        }

        #region Globals auto-complete
        private readonly List<string> _Globals = new List<string>();
        private bool GlobalsSorted;

        /// <summary>
        /// An alphabetically sorted list of all globals (objects, methods, etc.) externally added to this Lua instance
        /// </summary>
        /// <remarks>Members of globals are also listed. The formatting is optimized for text input auto-completion.</remarks>
        public IEnumerable<string> Globals
        {
            get
            {
                // Only sort list when necessary
                if (!GlobalsSorted)
                {
                    _Globals.Sort();
                    GlobalsSorted = true;
                }

                return _Globals;
            }
        }

        /// <summary>
        /// Adds an entry to <see cref="_Globals"/> (recursivley handles 2 levels of members)
        /// </summary>
        /// <param name="Path">The index accessor path ot the entry</param>
        /// <param name="OType">The type of the entry</param>
        /// <param name="RecursionCounter">How deep have we gone with recursion?</param>
        private void RegisterGlobal(string Path, Type OType, int RecursionCounter)
        {
            // If the type is a global method, list it directly
            if (OType == typeof(KopiLua.LuaNativeFunction))
            {
                // Format for easy method invocation
                _Globals.Add(Path + "(");
            }
            // If the type is a class or an interface and recursion hasn't been running too long, list the members
            else if ((OType.IsClass || OType.IsInterface) && OType != typeof(string) && RecursionCounter < 2)
            {
                #region Methods
                foreach (MethodInfo Method in OType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (Method.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (Method.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0) &&
                        // Exclude some generic .NET methods that wouldn't be very usefull in Lua
                        Method.Name != "GetType" && Method.Name != "GetHashCode" && Method.Name != "Equals" &&
                        Method.Name != "ToString" && Method.Name != "Clone" && Method.Name != "Dispose" &&
                        Method.Name != "GetEnumerator" && Method.Name != "CopyTo" &&
                        !Method.Name.StartsWith("get_", StringComparison.Ordinal) &&
                        !Method.Name.StartsWith("set_", StringComparison.Ordinal) &&
                        !Method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                        !Method.Name.StartsWith("remove_", StringComparison.Ordinal))
                    {
                        // Format for easy method invocation
                        string Command = Path + ":" + Method.Name + "(";
                        if (Method.GetParameters().Length == 0) Command += ")";
                        _Globals.Add(Command);
                    }
                }
                #endregion

                #region Fields
                foreach (FieldInfo Field in OType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (Field.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (Field.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0))
                    {
                        // Go into recursion for members
                        RegisterGlobal(Path + "." + Field.Name, Field.FieldType, RecursionCounter + 1);
                    }
                }
                #endregion

                #region Properties
                foreach (PropertyInfo Property in OType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (Property.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (Property.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0)
                        // Exclude some generic .NET properties that wouldn't be very usefull in Lua
                        && Property.Name != "Item")
                    {
                        // Go into recursion for members
                        RegisterGlobal(Path + "." + Property.Name, Property.PropertyType, RecursionCounter + 1);
                    }
                }
                #endregion
            }
            // Otherwise simply add the element to the list
            else _Globals.Add(Path);

            // List will need to be sorted on next access
            GlobalsSorted = false;
        }
        #endregion
        #region Getters
        /// <summary>
        /// Navigates a table in the top of the stack
        /// </summary>
        /// <returns>The value of the specified field</returns>
        internal object GetObject(string[] RemainingPath)
        {
            object ReturnValue = null;
            for (int i = 0; i < RemainingPath.Length; i++)
            {
                LuaCore.LuaPushString(LuaState, RemainingPath[i]);
                LuaCore.LuaGetTable(LuaState, -2);
                ReturnValue = Translator.GetObject(LuaState, -1);
                if (ReturnValue == null) break;
            }
            return ReturnValue;
        }
        
        /// <summary>
        /// Gets a numeric global variable
        /// </summary>
        public double GetNumber(string FullPath)
        {
            return (double)this[FullPath];
        }
        
        /// <summary>
        /// Gets a string global variable
        /// </summary>
        public string GetString(string FullPath)
        {
            return (string)this[FullPath];
        }
        
        /// <summary>
        /// Gets a table global variable
        /// </summary>
        public LuaTable GetTable(string FullPath)
        {
            return (LuaTable)this[FullPath];
        }
        
        /// <summary>
        /// Gets a table global variable as an object implementing the interfaceType interface
        /// </summary>
        public object GetTable(Type InterfaceType, string FullPath)
        {
            return CodeGeneration.Instance.GetClassInstance(InterfaceType, GetTable(FullPath));
        }
        
        /// <summary>
        /// Gets a function global variable
        /// </summary>
        public LuaFunction GetFunction(string FullPath)
        {
            object Obj = this[FullPath];
            return (Obj is KopiLua.LuaNativeFunction ? new LuaFunction((KopiLua.LuaNativeFunction)Obj, this) : (LuaFunction)Obj);
        }
        
        /// <summary>
        /// Gets a function global variable as a delegate of type delegateType
        /// </summary>
        public Delegate GetFunction(Type DelegateType, string FullPath)
        {
            return CodeGeneration.Instance.GetDelegate(DelegateType, GetFunction(FullPath));
        }
        #endregion
        #region Caller
        /// <summary>
        /// Calls the object as a function with the provided arguments,
        /// </summary>
        /// <returns>The Function's returned values inside an array</returns>
        internal object[] CallFunction(object Function, object[] Args)
        {
            return CallFunction(Function, Args, null);
        }

        /// <summary>
        /// Calls the object as a function with the provided arguments and 
        /// casting returned values to the types in returnTypes
        /// </summary>
        /// <returns>The function's casted returned values inside an array</returns>
        internal object[] CallFunction(object Function, object[] Args, Type[] ReturnTypes)
        {
            int NArgs = 0;
            int OldTop = LuaCore.LuaGetTop(LuaState);
            if (!LuaCore.LuaCheckStack(LuaState, Args.Length + 6))
            {
                throw new LuaException("Lua stack overflow");
            }

            Translator.Push(LuaState, Function);
            if (Args != null)
            {
                NArgs = Args.Length;
                for (int i = 0; i < Args.Length; i++)
                {
                    Translator.Push(LuaState, Args[i]);
                }
            }
            Executing = true;
            try
            {
                int Error = LuaCore.LuaPCall(LuaState, NArgs, -1, 0);
                if (Error != 0) ThrowExceptionFromError(OldTop);
            }
            finally { Executing = false; }

            if (ReturnTypes != null)
            {
                return Translator.PopValues(LuaState, OldTop, ReturnTypes);
            }
            else
            {
                return Translator.PopValues(LuaState, OldTop);
            }
        }
        #endregion
        #region Setters
        /// <summary>
        /// Navigates a table to set the value of one of its fields
        /// </summary>
        internal void SetObject(string[] RemainingPath, object Obj)
        {
            for (int i = 0; i < RemainingPath.Length - 1; i++)
            {
                LuaCore.LuaPushString(LuaState, RemainingPath[i]);
                LuaCore.LuaGetTable(LuaState, -2);
            }
            LuaCore.LuaPushString(LuaState, RemainingPath[RemainingPath.Length - 1]);
            Translator.Push(LuaState, Obj);
            LuaCore.LuaSetTable(LuaState, -3);
        }
        
        /// <summary>
        /// Creates a new table as a global variable or as a field inside an existing table
        /// </summary>
        public LuaTable NewTable(string FullPath)
        {
            string[] Path = FullPath.Split(new char[] { '.' });
            int OldTop = LuaCore.LuaGetTop(LuaState);
            if (Path.Length == 1)
            {
                LuaCore.LuaNewTable(LuaState);
                LuaCore.LuaSetGlobal(LuaState, FullPath);
            }
            else
            {
                LuaCore.LuaGetGlobal(LuaState, Path[0]);
                for (int i = 1; i < Path.Length - 1; i++)
                {
                    LuaCore.LuaPushString(LuaState, Path[i]);
                    LuaCore.LuaGetTable(LuaState, -2);
                }
                LuaCore.LuaPushString(LuaState, Path[Path.Length - 1]);
                LuaCore.LuaNewTable(LuaState);
                LuaCore.LuaSetTable(LuaState, -3);
            }
            LuaCore.LuaSetTop(LuaState, OldTop);

            return (LuaTable)this[FullPath];
        }

        public ListDictionary GetTableDict(LuaTable Table)
        {
            ListDictionary Dict = new ListDictionary();

            int OldTop = LuaCore.LuaGetTop(LuaState);
            Translator.Push(LuaState, Table);
            LuaCore.LuaPushNil(LuaState);
            while (LuaCore.LuaNext(LuaState, -2) != 0)
            {
                Dict[Translator.GetObject(LuaState, -2)] = Translator.GetObject(LuaState, -1);
                LuaCore.LuaSetTop(LuaState, -2);
            }
            LuaCore.LuaSetTop(LuaState, OldTop);

            return Dict;
        }
        #endregion
        #region ObjectManagement
        /// <summary>
        /// Lets go of a previously allocated reference to a table, function or userdata
        /// </summary>
        internal void DisposeObject(int Reference)
        {
            if (LuaState != null) LuaCore.LuaUnref(LuaState, Reference);
        }

        /// <summary>
        /// Gets a field of the table corresponding to the provided reference using rawget (do not use metatables)
        /// </summary>
        internal object RawGetObject(int Reference, string Field)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Reference);
            LuaCore.LuaPushString(LuaState, Field);
            LuaCore.LuaRawGet(LuaState, -2);
            object Obj = Translator.GetObject(LuaState, -1);
            LuaCore.LuaSetTop(LuaState, OldTop);
            return Obj;
        }
        
        /// <summary>
        /// Gets a field of the table or userdata corresponding to the provided reference
        /// </summary>
        internal object GetObject(int Reference, string Field)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Reference);
            object ReturnValue = GetObject(Field.Split(new char[] { '.' }));
            LuaCore.LuaSetTop(LuaState, OldTop);
            return ReturnValue;
        }
        
        /// <summary>
        /// Gets a numeric field of the table or userdata corresponding the the provided reference
        /// </summary>
        internal object GetObject(int Reference, object Field)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Reference);
            Translator.Push(LuaState, Field);
            LuaCore.LuaGetTable(LuaState, -2);
            object ReturnValue = Translator.GetObject(LuaState, -1);
            LuaCore.LuaSetTop(LuaState, OldTop);
            return ReturnValue;
        }
        
        /// <summary>
        /// Sets a field of the table or userdata corresponding the the provided reference
        /// to the provided value
        /// </summary>
        internal void SetObject(int Reference, string Field, object Obj)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Reference);
            SetObject(Field.Split(new char[] { '.' }), Obj);
            LuaCore.LuaSetTop(LuaState, OldTop);
        }
        
        /// <summary>
        /// Sets a numeric field of the table or userdata corresponding the the provided reference
        /// to the provided value
        /// </summary>
        internal void SetObject(int Reference, object Field, object Obj)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Reference);
            Translator.Push(LuaState, Field);
            Translator.Push(LuaState, Obj);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaSetTop(LuaState, OldTop);
        }
        #endregion

        /// <summary>
        /// Registers an object's method as a Lua function (global or table field)
        /// The method may have any signature
        /// </summary>
        public LuaFunction RegisterFunction(string Path, object Target, MethodBase Function)
        {
            //Fix for struct constructor by Alexander Kappner (link: http://luaforge.net/forum/forum.php?thread_id=2859&forum_id=145)

            // We leave nothing on the stack when we are done
            int OldTop = LuaCore.LuaGetTop(LuaState);

            LuaMethodWrapper Wrapper = new LuaMethodWrapper(Translator, Target, Function.DeclaringType, Function);
            Translator.Push(LuaState, new KopiLua.LuaNativeFunction(Wrapper.Call));

            this[Path] = Translator.GetObject(LuaState, -1);
            LuaFunction F = GetFunction(Path);

            LuaCore.LuaSetTop(LuaState, OldTop);

            return F;
        }

        /// <summary>
        /// Registers an object's method as a Lua function (global or table field)
        /// The method may have any signature
        /// </summary>
        public LuaFunction RegisterFunction(string Path, object Target, string Function)
        {
            return RegisterFunction(Path, Target, Target.GetType().GetMethod(Function));
        }

        /// <summary>
        /// Compares the two values referenced by ref1 and ref2 for equality
        /// </summary>
        internal bool CompareRef(int Ref1, int Ref2)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaGetRef(LuaState, Ref1);
            LuaCore.LuaGetRef(LuaState, Ref2);
            int Equal = LuaCore.LuaEqual(LuaState, -1, -2);
            LuaCore.LuaSetTop(LuaState, OldTop);
            return (Equal != 0);
        }

        internal void PushCSFunction(KopiLua.LuaNativeFunction function)
        {
            Translator.PushFunction(LuaState, function);
        }

        /// <summary>
        /// Push a value to the lua stack
        /// </summary>
        /// <param name="Obj">The object to push</param>
        public void Push(object Obj)
        {
            Translator.Push(LuaState, Obj);
        }

        #region IDisposable Members

        public virtual void Dispose()
        {
            if (Translator != null)
            {
                Translator.PendingEvents.Dispose();
                Translator = null;
            }

            this.Close();
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        #endregion

        public LuaThread NewThread()
        {
            return new LuaThread(LuaCore.LuaNewThread(LuaState), Translator);
        }
    }



}
