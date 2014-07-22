using System;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;

namespace LuaInterface
{
    /// <summary>
    /// Passes objects from the CLR to Lua and vice-versa
    /// </summary>
    public class ObjectTranslator
    {
        internal CheckType TypeChecker;

        /// <summary>
        /// Number of object to object (FIXME - it should be possible to get object address as an object #)
        /// </summary>
        public readonly Dictionary<int, object> Objects = new Dictionary<int, object>();

        /// <summary>
        /// Object to object number
        /// </summary>
        public readonly Dictionary<object, int> ObjectsBackMap = new Dictionary<object, int>();

        internal Lua Interpreter;
        private MetaFunctions MetaFunctions;
        private List<Assembly> Assemblies;
        private KopiLua.LuaNativeFunction RegisterTableFunction, UnregisterTableFunction, GetMethodSigFunction,
            GetConstructorSigFunction, ImportTypeFunction, LoadAssemblyFunction, CTypeFunction, EnumFromIntFunction;

        internal EventHandlerContainer PendingEvents = new EventHandlerContainer();

        public ObjectTranslator(Lua Interpreter, KopiLua.LuaState LuaState)
        {
            this.Interpreter = Interpreter;
            TypeChecker = new CheckType(this);
            MetaFunctions = new MetaFunctions(this);
            Assemblies = new List<Assembly>();

            ImportTypeFunction = new KopiLua.LuaNativeFunction(this.ImportType);
            LoadAssemblyFunction = new KopiLua.LuaNativeFunction(this.LoadAssembly);
            RegisterTableFunction = new KopiLua.LuaNativeFunction(this.RegisterTable);
            UnregisterTableFunction = new KopiLua.LuaNativeFunction(this.UnregisterTable);
            GetMethodSigFunction = new KopiLua.LuaNativeFunction(this.GetMethodSignature);
            GetConstructorSigFunction = new KopiLua.LuaNativeFunction(this.GetConstructorSignature);

            CTypeFunction = new KopiLua.LuaNativeFunction(this.CType);
            EnumFromIntFunction = new KopiLua.LuaNativeFunction(this.EnumFromInt);

            CreateLuaObjectList(LuaState);
            CreateIndexingMetaFunction(LuaState);
            CreateBaseClassMetatable(LuaState);
            CreateClassMetatable(LuaState);
            CreateFunctionMetatable(LuaState);

            //Don't want to be able to access Lua stuff.
            //SetGlobalFunctions(LuaState);
        }

        /// <summary>
        /// Sets up the list of objects in the Lua side
        /// </summary>
        private void CreateLuaObjectList(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaPushString(LuaState, "luaNet_objects");
            LuaCore.LuaNewTable(LuaState);
            LuaCore.LuaNewTable(LuaState);
            LuaCore.LuaPushString(LuaState, "__mode");
            LuaCore.LuaPushString(LuaState, "v");
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaSetMetatable(LuaState, -2);
            LuaCore.LuaSetTable(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX);
        }

        /// <summary>
        /// Registers the indexing function of CLR objects passed to Lua
        /// </summary>
        private void CreateIndexingMetaFunction(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaPushString(LuaState, "luaNet_indexfunction");
            LuaCore.LuaLDoString(LuaState, MetaFunctions.LuaIndexFunction);
            LuaCore.LuaRawSet(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX);
        }

        /// <summary>
        /// Creates the metatable for superclasses (the base field of registered tables)
        /// </summary>
        private void CreateBaseClassMetatable(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaLNewMetatable(LuaState, "luaNet_searchbase");
            LuaCore.LuaPushString(LuaState, "__gc");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.GCFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__tostring");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ToStringFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__index");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.BaseIndexFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__newindex");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.NewIndexFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaSetTop(LuaState, -2);
        }

        /// <summary>
        /// Creates the metatable for type references
        /// </summary>
        private void CreateClassMetatable(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaLNewMetatable(LuaState, "luaNet_class");
            LuaCore.LuaPushString(LuaState, "__gc");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.GCFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__tostring");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ToStringFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__index");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ClassIndexFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__newindex");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ClassNewIndexFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__call");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.CallConstructorFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaSetTop(LuaState, -2);
        }

        /// <summary>
        /// Registers the global functions used by CCLib.LuaInterface
        /// </summary>
        public void SetGlobalFunctions(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.IndexFunction);
            LuaCore.LuaSetGlobal(LuaState, "get_object_member");
            LuaCore.LuaPushStdCallCFunction(LuaState, ImportTypeFunction);
            LuaCore.LuaSetGlobal(LuaState, "import_type");
            LuaCore.LuaPushStdCallCFunction(LuaState, LoadAssemblyFunction);
            LuaCore.LuaSetGlobal(LuaState, "load_assembly");
            LuaCore.LuaPushStdCallCFunction(LuaState, RegisterTableFunction);
            LuaCore.LuaSetGlobal(LuaState, "make_object");
            LuaCore.LuaPushStdCallCFunction(LuaState, UnregisterTableFunction);
            LuaCore.LuaSetGlobal(LuaState, "free_object");
            LuaCore.LuaPushStdCallCFunction(LuaState, GetMethodSigFunction);
            LuaCore.LuaSetGlobal(LuaState, "get_method_bysig");
            LuaCore.LuaPushStdCallCFunction(LuaState, GetConstructorSigFunction);
            LuaCore.LuaSetGlobal(LuaState, "get_constructor_bysig");
            LuaCore.LuaPushStdCallCFunction(LuaState, CTypeFunction);
            LuaCore.LuaSetGlobal(LuaState, "ctype");
            LuaCore.LuaPushStdCallCFunction(LuaState, EnumFromIntFunction);
            LuaCore.LuaSetGlobal(LuaState, "enum");

        }

        /// <summary>
        /// Creates the metatable for delegates
        /// </summary>
        private void CreateFunctionMetatable(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaLNewMetatable(LuaState, "luaNet_function");
            LuaCore.LuaPushString(LuaState, "__gc");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.GCFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaPushString(LuaState, "__call");
            LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ExecDelegateFunction);
            LuaCore.LuaSetTable(LuaState, -3);
            LuaCore.LuaSetTop(LuaState, -2);
        }

        /// <summary>
        /// Passes errors (argument e) to the Lua interpreter
        /// </summary>
        internal void ThrowError(KopiLua.LuaState LuaState, object e)
        {
            // If the argument is a mere string, we are free to add extra info to it
            //(as opposed to some private C# exception object or somesuch, which we just pass up)
            if (e is string)
            {
                // We use this to remove anything pushed by LuaLWhere
                int oldTop = LuaCore.LuaGetTop(LuaState);

                // Stack frame #1 is our C# wrapper, so not very interesting to the user
                // Stack frame #2 must be the lua code that called us, so that's what we want to use
                LuaCore.LuaLWhere(LuaState, 2);
                object[] curlev = PopValues(LuaState, oldTop);

                if (curlev.Length > 0) e = curlev[0].ToString() + e;
            }

            Push(LuaState, e);
            LuaCore.LuaError(LuaState);
        }

        /// <summary>
        /// Implementation of load_assembly. Throws an error if the assembly is not found.
        /// </summary>
        private int LoadAssembly(KopiLua.LuaState LuaState)
        {
            try
            {
                string AssemName = LuaCore.LuaToString(LuaState, 1);

                Assembly Assembly = null;

                try
                {
                    Assembly = Assembly.Load(AssemName);
                }
                catch (BadImageFormatException)
                {
                    // The assemblyName was invalid.  It is most likely a path.
                }

                if (Assembly == null)
                {

                    Assembly = Assembly.Load(AssemblyName.GetAssemblyName(AssemName));
                }

                if (Assembly != null && !Assemblies.Contains(Assembly))
                {
                    Assemblies.Add(Assembly);
                }
            }
            catch (Exception e)
            {
                ThrowError(LuaState, e);
            }

            return 0;
        }

        internal Type FindType(string ClassName)
        {
            if (ClassName.StartsWith("out ") || ClassName.StartsWith("ref "))
            {
                Type LuaType = FindType(ClassName.Substring(4));
                if (LuaType != null) return LuaType.MakeByRefType();
                return null;
            }

            foreach (Assembly Assembly in Assemblies)
            {
                Type Klass = Assembly.GetType(ClassName);
                if (Klass != null)
                {
                    return Klass;
                }
            }
            return null;
        }

        /// <summary>
        /// Implementation of import_type. Returns 
        /// </summary>
        /// <returns>Null if the type is not found</returns>
        private int ImportType(KopiLua.LuaState LuaState)
        {
            string ClassName = LuaCore.LuaToString(LuaState, 1);
            Type Klass = FindType(ClassName);
            if (Klass != null)
            {
                PushType(LuaState, Klass);
            }
            else
            {
                LuaCore.LuaPushNil(LuaState);
            }
            return 1;
        }

        /// <summary>
        /// Implementation of make_object. 
        /// Registers a table (first argument in the stack) as an object 
        /// subclassing the type passed as second argument in the stack.
        /// </summary>
        /// <param name="LuaState"></param>
        /// <returns></returns>
        private int RegisterTable(KopiLua.LuaState LuaState)
        {
            if (LuaCore.LuaType(LuaState, 1) == LuaTypes.LUA_TTABLE)
            {
                LuaTable LuaTable = GetTable(LuaState, 1);
                string SuperclassName = LuaCore.LuaToString(LuaState, 2);

                if (SuperclassName != null)
                {
                    Type Klass = FindType(SuperclassName);
                    if (Klass != null)
                    {
                        // Creates and pushes the object in the stack, setting
                        // it as the  metatable of the first argument
                        object Obj = CodeGeneration.Instance.GetClassInstance(Klass, LuaTable);
                        PushObject(LuaState, Obj, "luaNet_metatable");
                        LuaCore.LuaNewTable(LuaState);
                        LuaCore.LuaPushString(LuaState, "__index");
                        LuaCore.LuaPushValue(LuaState, -3);
                        LuaCore.LuaSetTable(LuaState, -3);
                        LuaCore.LuaPushString(LuaState, "__newindex");
                        LuaCore.LuaPushValue(LuaState, -3);
                        LuaCore.LuaSetTable(LuaState, -3);
                        LuaCore.LuaSetMetatable(LuaState, 1);
                        // Pushes the object again, this time as the base field
                        // of the table and with the luaNet_searchbase metatable
                        LuaCore.LuaPushString(LuaState, "base");
                        int Index = AddObject(Obj);
                        PushNewObject(LuaState, Obj, Index, "luaNet_searchbase");
                        LuaCore.LuaRawSet(LuaState, 1);
                    }
                    else
                    {
                        ThrowError(LuaState, "register_table: can not find superclass '" + SuperclassName + "'");
                    }
                }
                else
                {
                    ThrowError(LuaState, "register_table: superclass name can not be null");
                }
            }
            else
            {
                ThrowError(LuaState, "register_table: first arg is not a table");
            }

            return 0;
        }

        /// <summary>
        /// Implementation of free_object. Clears the metatable and the base field, 
        /// freeing the created object for garbage-collection
        /// </summary>
        private int UnregisterTable(KopiLua.LuaState LuaState)
        {
            try
            {
                if (LuaCore.LuaGetMetatable(LuaState, 1) != 0)
                {
                    LuaCore.LuaPushString(LuaState, "__index");
                    LuaCore.LuaGetTable(LuaState, -2);

                    object Obj = GetRawNetObject(LuaState, -1);
                    if (Obj == null) ThrowError(LuaState, "unregister_table: arg is not valid table");

                    FieldInfo LuaTableField = Obj.GetType().GetField("__LuaInterface_luaTable");
                    if (LuaTableField == null) ThrowError(LuaState, "unregister_table: arg is not valid table");

                    LuaTableField.SetValue(Obj, null);
                    LuaCore.LuaPushNil(LuaState);
                    LuaCore.LuaSetMetatable(LuaState, 1);
                    LuaCore.LuaPushString(LuaState, "base");
                    LuaCore.LuaPushNil(LuaState);
                    LuaCore.LuaSetTable(LuaState, 1);
                }
                else
                {
                    ThrowError(LuaState, "unregister_table: arg is not valid table");
                }
            }
            catch (Exception e)
            {
                ThrowError(LuaState, e.Message);
            }
            return 0;
        }

        /// <summary>
        /// Implementation of get_method_bysig. 
        /// </summary>
        /// <returns>Null if no matching method is found</returns>
        private int GetMethodSignature(KopiLua.LuaState LuaState)
        {
            IReflect Klass; object Target;
            int UserData = LuaCore.LuaNewCheckupData(LuaState, 1, "luaNet_class");
            if (UserData != -1)
            {
                Klass = (IReflect)Objects[UserData];
                Target = null;
            }
            else
            {
                Target = GetRawNetObject(LuaState, 1);
                if (Target == null)
                {
                    ThrowError(LuaState, "get_method_bysig: first arg is not type or object reference");
                    LuaCore.LuaPushNil(LuaState);
                    return 1;
                }
                Klass = Target.GetType();
            }
            string MethodName = LuaCore.LuaToString(LuaState, 2);
            Type[] Signature = new Type[LuaCore.LuaGetTop(LuaState) - 2];
            for (int i = 0; i < Signature.Length; i++)
            {
                string TypeName = LuaCore.LuaToString(LuaState, i + 3);
                Signature[i] = FindType(TypeName);
                if (Signature[i] == null)
                {
                    ThrowError(LuaState, String.Format("get_method_bysig: type not found: {0}", TypeName));
                }
            }
            try
            {
                MethodInfo Method = Klass.GetMethod(
                    MethodName, BindingFlags.Public | BindingFlags.Static |
                    BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase,
                    null, Signature, null
                );
                PushFunction(LuaState, new KopiLua.LuaNativeFunction((new LuaMethodWrapper(this, Target, Klass, Method)).Call));

            }
            catch (Exception e)
            {
                ThrowError(LuaState, e);
                LuaCore.LuaPushNil(LuaState);
            }
            return 1;
        }

        /// <summary>
        /// Implementation of get_constructor_bysig
        /// </summary>
        /// <returns>Null if no matching constructor is found</returns>
        private int GetConstructorSignature(KopiLua.LuaState LuaState)
        {
            IReflect Klass = null;
            int UserData = LuaCore.LuaNewCheckupData(LuaState, 1, "luaNet_class");
            if (UserData != -1)
            {
                Klass = (IReflect)Objects[UserData];
            }
            if (Klass == null)
            {
                ThrowError(LuaState, "get_constructor_bysig: first arg is invalid type reference");
            }
            Type[] Signature = new Type[LuaCore.LuaGetTop(LuaState) - 1];
            for (int i = 0; i < Signature.Length; i++)
            {
                string TypeName = LuaCore.LuaToString(LuaState, i + 2);
                Signature[i] = FindType(TypeName);
                if (Signature[i] == null)
                {
                    ThrowError(LuaState, String.Format("get_constructor_bysig: type not found: {0}", TypeName));
                }
            }
            try
            {
                ConstructorInfo constructor = Klass.UnderlyingSystemType.GetConstructor(Signature);
                PushFunction(LuaState, new KopiLua.LuaNativeFunction((new LuaMethodWrapper(this, null, Klass, constructor)).Call));
            }
            catch (Exception e)
            {
                ThrowError(LuaState, e);
                LuaCore.LuaPushNil(LuaState);
            }
            return 1;
        }


        private Type TypeOf(KopiLua.LuaState LuaState, int Index)
        {
            int UserData = LuaCore.LuaNewCheckupData(LuaState, 1, "luaNet_class");
            if (UserData == -1)
            {
                return null;
            }
            else
            {
                ProxyType Pt = (ProxyType)Objects[UserData];
                return Pt.UnderlyingSystemType;
            }
        }

        public int PushError(KopiLua.LuaState LuaState, string Msg)
        {
            LuaCore.LuaPushNil(LuaState);
            LuaCore.LuaPushString(LuaState, Msg);
            return 2;
        }

        private int CType(KopiLua.LuaState LuaState)
        {
            Type Typ = TypeOf(LuaState, 1);
            if (Typ == null)
            {
                return PushError(LuaState, "not a CLR class");
            }
            PushObject(LuaState, Typ, "luaNet_metatable");
            return 1;
        }

        private int EnumFromInt(KopiLua.LuaState LuaState)
        {
            Type Typ = TypeOf(LuaState, 1);
            if (Typ == null || !Typ.IsEnum)
            {
                return PushError(LuaState, "not an enum");
            }
            object Obj = null;
            LuaTypes LuaType = LuaCore.LuaType(LuaState, 2);
            if (LuaType == LuaTypes.LUA_TNUMBER)
            {
                int IVal = (int)LuaCore.LuaToNumber(LuaState, 2);
                Obj = Enum.ToObject(Typ, IVal);
            }
            else
            {
                if (LuaType == LuaTypes.LUA_TSTRING)
                {
                    string SFlags = LuaCore.LuaToString(LuaState, 2);
                    string Err = null;
                    try
                    {
                        Obj = Enum.Parse(Typ, SFlags);
                    }
                    catch (ArgumentException e)
                    {
                        Err = e.Message;
                    }
                    if (Err != null)
                    {
                        return PushError(LuaState, Err);
                    }
                }
                else
                {
                    return PushError(LuaState, "second argument must be a integer or a string");
                }
            }
            PushObject(LuaState, Obj, "luaNet_metatable");
            return 1;
        }

        /// <summary>
        /// Pushes a type reference into the stack
        /// </summary>
        internal void PushType(KopiLua.LuaState LuaState, Type Typ)
        {
            PushObject(LuaState, new ProxyType(Typ), "luaNet_class");
        }

        /// <summary>
        /// Pushes a delegate into the stack
        /// </summary>
        internal void PushFunction(KopiLua.LuaState LuaState, KopiLua.LuaNativeFunction Function)
        {
            PushObject(LuaState, Function, "luaNet_function");
        }

        /// <summary>
        /// Pushes a CLR object into the Lua stack as an userdata with the provided metatable
        /// </summary>
        internal void PushObject(KopiLua.LuaState LuaState, object Obj, string Metatable)
        {
            int Index = -1;
            // Pushes nil
            if (Obj == null)
            {
                LuaCore.LuaPushNil(LuaState);
                return;
            }

            // Object already in the list of Lua objects? Push the stored reference.
            bool Found = ObjectsBackMap.TryGetValue(Obj, out Index);
            if (Found)
            {
                LuaCore.LuaLGetMetatable(LuaState, "luaNet_objects");
                LuaCore.LuaRawGetI(LuaState, -1, Index);

                // Note: starting with lua5.1 the garbage collector may remove weak reference items (such as our luaNet_objects values) when the initial GC sweep
                // occurs, but the actual call of the __gc finalizer for that object may not happen until a little while later.  During that window we might call
                // this routine and find the element missing from luaNet_objects, but collectObject() has not yet been called.  In that case, we go ahead and call collect
                // object here
                // did we find a non nil object in our table? if not, we need to call collect object

                LuaTypes LuaType = LuaCore.LuaType(LuaState, -1);
                if (LuaType != LuaTypes.LUA_TNIL)
                {
                    LuaCore.LuaRemove(LuaState, -2);     // drop the metatable - we're going to leave our object on the stack

                    return;
                }

                // MetaFunctions.dumpStack(this, LuaState);
                LuaCore.LuaRemove(LuaState, -1);    // remove the nil object value
                LuaCore.LuaRemove(LuaState, -1);    // remove the metatable

                CollectObject(Obj, Index);            // Remove from both our tables and fall out to get a new ID
            }
            Index = AddObject(Obj);

            PushNewObject(LuaState, Obj, Index, Metatable);
        }

        /// <summary>
        /// Pushes a new object into the Lua stack with the provided metatable
        /// </summary>
        private void PushNewObject(KopiLua.LuaState LuaState, object Obj, int Index, string Metatable)
        {
            if (Metatable == "luaNet_metatable")
            {
                // Gets or creates the metatable for the object's type
                LuaCore.LuaLGetMetatable(LuaState, Obj.GetType().AssemblyQualifiedName);

                if (LuaCore.LuaIsNil(LuaState, -1))
                {
                    LuaCore.LuaSetTop(LuaState, -2);
                    LuaCore.LuaLNewMetatable(LuaState, Obj.GetType().AssemblyQualifiedName);
                    LuaCore.LuaPushString(LuaState, "cache");
                    LuaCore.LuaNewTable(LuaState);
                    LuaCore.LuaRawSet(LuaState, -3);
                    LuaCore.LuaPushLightUserData(LuaState, LuaCore.LuaNetGetTag());
                    LuaCore.LuaPushNumber(LuaState, 1);
                    LuaCore.LuaRawSet(LuaState, -3);
                    LuaCore.LuaPushString(LuaState, "__index");
                    LuaCore.LuaPushString(LuaState, "luaNet_indexfunction");
                    LuaCore.LuaRawGet(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX);
                    LuaCore.LuaRawSet(LuaState, -3);
                    LuaCore.LuaPushString(LuaState, "__gc");
                    LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.GCFunction);
                    LuaCore.LuaRawSet(LuaState, -3);
                    LuaCore.LuaPushString(LuaState, "__tostring");
                    LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.ToStringFunction);
                    LuaCore.LuaRawSet(LuaState, -3);
                    LuaCore.LuaPushString(LuaState, "__newindex");
                    LuaCore.LuaPushStdCallCFunction(LuaState, MetaFunctions.NewIndexFunction);
                    LuaCore.LuaRawSet(LuaState, -3);
                }
            }
            else
            {
                LuaCore.LuaLGetMetatable(LuaState, Metatable);
            }

            // Stores the object index in the Lua list and pushes the
            // index into the Lua stack
            LuaCore.LuaLGetMetatable(LuaState, "luaNet_objects");
            LuaCore.LuaNetNewUData(LuaState, Index);
            LuaCore.LuaPushValue(LuaState, -3);
            LuaCore.LuaRemove(LuaState, -4);
            LuaCore.LuaSetMetatable(LuaState, -2);
            LuaCore.LuaPushValue(LuaState, -1);
            LuaCore.LuaRawSetI(LuaState, -3, Index);
            LuaCore.LuaRemove(LuaState, -2);
        }

        /// <summary>
        /// Gets an object from the Lua stack with the desired type, if it matches.
        /// </summary>
        /// <returns>Null if no matches</returns>
        internal object GetAsType(KopiLua.LuaState LuaState, int StackPos, Type ParamType)
        {
            ExtractValue Extractor = TypeChecker._CheckType(LuaState, StackPos, ParamType);
            if (Extractor != null) return Extractor(LuaState, StackPos);

            return null;
        }


        /// <summary>
        /// Given the Lua int ID for an object remove it from our maps
        /// </summary>
        internal void CollectObject(int UData)
        {
            object Obj;
            bool Found = Objects.TryGetValue(UData, out Obj);

            // The other variant of collectObject might have gotten here first, in that case we will silently ignore the missing entry
            if (Found)
            {
                CollectObject(Obj, UData);
            }
        }


        /// <summary>
        /// Given an object reference, remove it from our maps
        /// </summary>
        void CollectObject(object Obj, int UData)
        {
            Objects.Remove(UData);
            ObjectsBackMap.Remove(Obj);
        }


        /// <summary>
        /// We want to ensure that objects always have a unique ID
        /// </summary>
        int NextObj = 0;

        int AddObject(object obj)
        {
            // New object: inserts it in the list
            int Index = NextObj++;

            Objects[Index] = obj;
            ObjectsBackMap[obj] = Index;

            return Index;
        }

        /// <summary>
        /// Gets an object from the Lua stack according to its Lua type.
        /// </summary>
        public object GetObject(KopiLua.LuaState LuaState, int Index)
        {
            LuaTypes Type = LuaCore.LuaType(LuaState, Index);
            switch (Type)
            {
                case LuaTypes.LUA_TNUMBER:
                    {
                        return LuaCore.LuaToNumber(LuaState, Index);
                    }
                case LuaTypes.LUA_TSTRING:
                    {
                        return LuaCore.LuaToString(LuaState, Index);
                    }
                case LuaTypes.LUA_TBOOLEAN:
                    {
                        return LuaCore.LuaToBoolean(LuaState, Index);
                    }
                case LuaTypes.LUA_TTABLE:
                    {
                        return GetTable(LuaState, Index);
                    }
                case LuaTypes.LUA_TFUNCTION:
                    {
                        return getFunction(LuaState, Index);
                    }
                case LuaTypes.LUA_TUSERDATA:
                    {
                        int udata = LuaCore.LuaNetToNetObject(LuaState, Index);
                        if (udata != -1)
                        {
                            return Objects[udata];
                        }
                        else
                        {
                            return getUserData(LuaState, Index);
                        }
                    }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets the table in the index positon of the Lua stack.
        /// </summary>
        internal LuaTable GetTable(KopiLua.LuaState LuaState, int Index)
        {
            LuaCore.LuaPushValue(LuaState, Index);
            return new LuaTable(LuaCore.LuaRef(LuaState, 1), Interpreter);
        }

        /// <summary>
        /// Gets the userdata in the index positon of the Lua stack.
        /// </summary>
        internal LuaUserData getUserData(KopiLua.LuaState LuaState, int Index)
        {
            LuaCore.LuaPushValue(LuaState, Index);
            return new LuaUserData(LuaCore.LuaRef(LuaState, 1), Interpreter);
        }

        /// <summary>
        /// Gets the function in the index positon of the Lua stack.
        /// </summary>
        internal LuaFunction getFunction(KopiLua.LuaState LuaState, int Index)
        {
            LuaCore.LuaPushValue(LuaState, Index);
            return new LuaFunction(LuaCore.LuaRef(LuaState, 1), Interpreter);
        }

        /// <summary>
        /// Gets the CLR object in the index positon of the Lua stack
        /// </summary>
        /// <returns>CLR object with delegates as Lua functions.</returns>
        internal object GetNetObject(KopiLua.LuaState LuaState, int Index)
        {
            int Idx = LuaCore.LuaNetToNetObject(LuaState, Index);
            if (Idx != -1)
            {
                return Objects[Idx];
            }

            return null;
        }

        /// <summary>
        /// Gets the CLR object in the index positon of the Lua stack.
        /// </summary>
        /// <returns>CLR object with delegates as is</returns>
        internal object GetRawNetObject(KopiLua.LuaState LuaState, int Index)
        {
            int Idx = LuaCore.LuaNewRawNewObj(LuaState, Index);
            if (Idx != -1)
            {
                return Objects[Idx];
            }
            return null;
        }

        /// <summary>
        /// Pushes the entire array into the Lua stack.
        /// </summary>
        /// <returns>Number of elements pushed</returns>
        internal int ReturnValues(KopiLua.LuaState LuaState, object[] ReturnValues)
        {
            if (LuaCore.LuaCheckStack(LuaState, ReturnValues.Length + 5))
            {
                for (int i = 0; i < ReturnValues.Length; i++)
                {
                    Push(LuaState, ReturnValues[i]);
                }
                return ReturnValues.Length;
            }

            return 0;
        }

        /// <summary>
        /// Gets the values from the provided index to the top of the stack
        /// </summary>
        /// <returns>Values as an array</returns>
        internal object[] PopValues(KopiLua.LuaState LuaState, int OldTop)
        {
            int NewTop = LuaCore.LuaGetTop(LuaState);
            if (OldTop == NewTop)
            {
                return null;
            }
            else
            {
                ArrayList ReturnValues = new ArrayList();
                for (int i = OldTop + 1; i <= NewTop; i++)
                {
                    ReturnValues.Add(GetObject(LuaState, i));
                }
                LuaCore.LuaSetTop(LuaState, OldTop);
                return ReturnValues.ToArray();
            }
        }

        /// <summary>
        /// Gets the values from the provided index to the top of the stack and returns them in an 
        /// </summary>
        /// <returns>The values in an array, casting them to the provided types</returns>
        internal object[] PopValues(KopiLua.LuaState LuaState, int OldTop, Type[] PopTypes)
        {
            int NewTop = LuaCore.LuaGetTop(LuaState);
            if (OldTop == NewTop)
            {
                return null;
            }
            else
            {
                int ITypes;
                ArrayList ReturnValues = new ArrayList();
                if (PopTypes[0] == typeof(void))
                {
                    ITypes = 1;
                }
                else
                {
                    ITypes = 0;
                }
                for (int i = OldTop + 1; i <= NewTop; i++)
                {
                    ReturnValues.Add(GetAsType(LuaState, i, PopTypes[ITypes]));
                    ITypes++;
                }

                LuaCore.LuaSetTop(LuaState, OldTop);
                return ReturnValues.ToArray();
            }
        }

        // kevinh - the following line doesn't work for remoting proxies - they always return a match for 'is'
        // else if(o is ILuaGeneratedType)
        static bool IsILua(object o)
        {
            if (o is ILuaGeneratedType)
            {
                // Make sure we are _really_ ILuaGenerated
                return (o.GetType().GetInterface("ILuaGeneratedType") != null);
            }

            return false;
        }

        /// <summary>
        /// Pushes the object into the Lua stack according to its type.
        /// </summary>
        internal void Push(KopiLua.LuaState LuaState, object Obj)
        {
            if (Obj == null)
            {
                LuaCore.LuaPushNil(LuaState);
            }
            else if (
                Obj is sbyte || Obj is byte || Obj is short || Obj is ushort ||
                Obj is int || Obj is uint || Obj is long || Obj is float ||
                Obj is ulong || Obj is decimal || Obj is double
            )
            {
                double D = Convert.ToDouble(Obj);
                LuaCore.LuaPushNumber(LuaState, D);
            }
            else if (Obj is char)
            {
                double D = (char)Obj;
                LuaCore.LuaPushNumber(LuaState, D);
            }
            else if (Obj is string)
            {
                string Str = (string)Obj;
                LuaCore.LuaPushString(LuaState, Str);
            }
            else if (Obj is bool)
            {
                bool B = (bool)Obj;
                LuaCore.LuaPushBoolean(LuaState, B);
            }
            else if (IsILua(Obj))
            {
                (((ILuaGeneratedType)Obj).GetLuaTable()).Push(LuaState, this);
            }
            else if (Obj is KopiLua.LuaNativeFunction)
            {
                PushFunction(LuaState, (KopiLua.LuaNativeFunction)Obj);
            }
            else if (Obj is ILuaPushable)
            {
                ((ILuaPushable)Obj).Push(LuaState, this);
            }
            else
            {
                PushObject(LuaState, Obj, "luaNet_metatable");
            }
        }

        /// <summary>
        /// Checks if the method matches the arguments in the Lua stack, 
        /// getting the arguments if it does
        /// </summary>
        internal bool MatchParameters(KopiLua.LuaState LuaState, MethodBase Method, ref MethodCache MethCache)
        {
            return MetaFunctions.MatchParameters(LuaState, Method, ref MethCache);
        }

        internal Array TableToArray(object LuaParamValue, Type ParamArrayType)
        {
            return MetaFunctions.TableToArray(LuaParamValue, ParamArrayType);
        }
    }
}
