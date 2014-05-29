using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LuaInterface
{
    /// <summary>
    /// Functions used in the metatables of userdata representing CLR objects
    /// </summary>
    class MetaFunctions
    {
        /// <summary>
        /// __index metafunction for CLR objects. Implemented in Lua.
        /// </summary>
        internal static string LuaIndexFunction =
        @"
        local function index(obj,name)
            local meta=getmetatable(obj)
            local cached=meta.cache[name]
            if cached then
               return cached
            else
               local value,isFunc = get_object_member(obj, name)
               if value==nil and type(isFunc)=='string' then error(isFunc, 2) end
               if isFunc then
                meta.cache[name]=value
               end
               return value
             end
        end
        return index";

        private ObjectTranslator Translator;
        private Hashtable MemberCache = new Hashtable();

        internal KopiLua.LuaNativeFunction GCFunction, IndexFunction, NewIndexFunction,
            BaseIndexFunction, ClassIndexFunction, ClassNewIndexFunction,
            ExecDelegateFunction, CallConstructorFunction, ToStringFunction;

        public MetaFunctions(ObjectTranslator Translator)
        {
            this.Translator = Translator;

            GCFunction = new KopiLua.LuaNativeFunction(this.CollectObject);
            ToStringFunction = new KopiLua.LuaNativeFunction(this.ToString);
            IndexFunction = new KopiLua.LuaNativeFunction(this.GetMethod);
            NewIndexFunction = new KopiLua.LuaNativeFunction(this.SetFieldOrProperty);
            BaseIndexFunction = new KopiLua.LuaNativeFunction(this.GetBaseMethod);
            CallConstructorFunction = new KopiLua.LuaNativeFunction(this.CallConstructor);
            ClassIndexFunction = new KopiLua.LuaNativeFunction(this.GetClassMethod);
            ClassNewIndexFunction = new KopiLua.LuaNativeFunction(this.SetClassFieldOrProperty);
            ExecDelegateFunction = new KopiLua.LuaNativeFunction(this.RunFunctionDelegate);
        }

        #region Meta Functions
        /// <summary>
        /// __call metafunction of CLR delegates, retrieves and calls the delegate.
        /// </summary>
        private int RunFunctionDelegate(KopiLua.LuaState LuaState)
        {
            KopiLua.LuaNativeFunction Func = (KopiLua.LuaNativeFunction)Translator.GetRawNetObject(LuaState, 1);
            LuaCore.LuaRemove(LuaState, 1);
            return Func(LuaState);
        }
        
        /// <summary>
        /// __gc metafunction of CLR objects.
        /// </summary>
        private int CollectObject(KopiLua.LuaState LuaState)
        {
            int UserData = LuaCore.LuaNewRawNewObj(LuaState, 1);
            if (UserData != -1)
            {
                Translator.CollectObject(UserData);
            }
            return 0;
        }
        
        /// <summary>
        /// __tostring metafunction of CLR objects.
        /// </summary>
        private int ToString(KopiLua.LuaState LuaState)
        {
            object Obj = Translator.GetRawNetObject(LuaState, 1);
            if (Obj != null)
            {
                Translator.Push(LuaState, Obj.ToString() + ": " + Obj.GetHashCode());
            }
            else
            {
                LuaCore.LuaPushNil(LuaState);
            }
            return 1;
        }

        /// <summary>
        /// Called by the __index metafunction of CLR objects in case the
        /// method is not cached or it is a field/property/event.
        /// Receives the object and the member name as arguments and returns
        /// either the value of the member or a delegate to call it.
        /// </summary>
        /// <returns>The member or nil</returns>
        private int GetMethod(KopiLua.LuaState LuaState)
        {
            object Obj = Translator.GetRawNetObject(LuaState, 1);
            if (Obj == null)
            {
                Translator.ThrowError(LuaState, "trying to index an invalid object reference");
                LuaCore.LuaPushNil(LuaState);
                return 1;
            }

            object Index = Translator.GetObject(LuaState, 2);

            string MethodName = Index as string;
            Type ObjType = Obj.GetType();

            // Handle the most common case, looking up the method by name.

            // CP: This will fail when using indexers and attempting to get a value with the same name as a property of the object,
            // ie: xmlelement['item'] <- item is a property of xmlelement
            try
            {
                if (MethodName != null && IsMemberPresent(ObjType, MethodName))
                {
                    return GetMember(LuaState, ObjType, Obj, MethodName, BindingFlags.Instance | BindingFlags.IgnoreCase);
                }
            }
            catch { }
            bool Failed = true;

            // Try to access by array if the type is right and index is an int (lua numbers always come across as double)
            if (ObjType.IsArray && Index is double)
            {
                int IntIndex = (int)((double)Index);
                Array ObjArray = Obj as Array;

                if (IntIndex >= ObjArray.Length)
                {
                    return Translator.PushError(LuaState, "array index out of bounds: " + IntIndex + " " + ObjArray.Length);
                }

                object Val = ObjArray.GetValue(IntIndex);
                Translator.Push(LuaState, Val);
                Failed = false;
            }
            else
            {
                // Try to use get_Item to index into this .net object
                //MethodInfo getter = objType.GetMethod("get_Item");
                // issue here is that there may be multiple indexers..
                MethodInfo[] Methods = ObjType.GetMethods();

                foreach (MethodInfo MInfo in Methods)
                {
                    if (MInfo.Name == "get_Item")
                    {
                        //check if the signature matches the input
                        if (MInfo.GetParameters().Length == 1)
                        {
                            MethodInfo Getter = MInfo;
                            ParameterInfo[] ActualParams = (Getter != null) ? Getter.GetParameters() : null;
                            if (ActualParams == null || ActualParams.Length != 1)
                            {
                                return Translator.PushError(LuaState, "method not found (or no indexer): " + Index);
                            }
                            else
                            {
                                // Get the index in a form acceptable to the getter
                                Index = Translator.GetAsType(LuaState, 2, ActualParams[0].ParameterType);
                                // Just call the indexer - if out of bounds an exception will happen
                                try
                                {
                                    object result = Getter.Invoke(Obj, new object[] { Index });
                                    Translator.Push(LuaState, result);
                                    Failed = false;
                                }
                                catch (TargetInvocationException e)
                                {
                                    // Provide a more readable description for the common case of key not found
                                    if (e.InnerException is KeyNotFoundException)
                                    {
                                        return Translator.PushError(LuaState, "key '" + Index + "' not found ");
                                    }
                                    else
                                    {
                                        return Translator.PushError(LuaState, "exception indexing '" + Index + "' " + e.Message);
                                    }


                                }
                            }
                        }
                    }
                }


            }
            if (Failed)
            {
                return Translator.PushError(LuaState, "cannot find " + Index);
            }
            LuaCore.LuaPushBoolean(LuaState, false);
            return 2;
        }

        /// <summary>
        /// __index metafunction of base classes (the base field of Lua tables).
        /// Adds a prefix to the method name to call the base version of the method.
        /// </summary>
        private int GetBaseMethod(KopiLua.LuaState LuaState)
        {
            object Obj = Translator.GetRawNetObject(LuaState, 1);
            if (Obj == null)
            {
                Translator.ThrowError(LuaState, "trying to index an invalid object reference");
                LuaCore.LuaPushNil(LuaState);
                LuaCore.LuaPushBoolean(LuaState, false);
                return 2;
            }

            string MethodName = LuaCore.LuaToString(LuaState, 2);
            if (MethodName == null)
            {
                LuaCore.LuaPushNil(LuaState);
                LuaCore.LuaPushBoolean(LuaState, false);
                return 2;
            }

            GetMember(LuaState, Obj.GetType(), Obj, "__LuaInterface_base_" + MethodName, BindingFlags.Instance | BindingFlags.IgnoreCase);
            LuaCore.LuaSetTop(LuaState, -2);

            if (LuaCore.LuaType(LuaState, -1) == LuaTypes.LUA_TNIL)
            {
                LuaCore.LuaSetTop(LuaState, -2);
                return GetMember(LuaState, Obj.GetType(), Obj, MethodName, BindingFlags.Instance | BindingFlags.IgnoreCase);
            }

            LuaCore.LuaPushBoolean(LuaState, false);
            return 2;
        }

        /// <summary>
        /// Pushes the value of a member or a delegate to call it, depending on the type of
        /// the member. Works with static or instance members.
        /// Uses reflection to find members, and stores the reflected MemberInfo object in
        /// a cache (indexed by the type of the object and the name of the member).
        /// </summary>
        private int GetMember(KopiLua.LuaState LuaState, IReflect ObjType, object Obj, string MethodName, BindingFlags BindingType)
        {
            bool ImplicitStatic = false;
            MemberInfo Member = null;
            object CachedMember = CheckMemberCache(MemberCache, ObjType, MethodName);
            
            if (CachedMember is KopiLua.LuaNativeFunction)
            {
                Translator.PushFunction(LuaState, (KopiLua.LuaNativeFunction)CachedMember);
                Translator.Push(LuaState, true);
                return 2;
            }
            else if (CachedMember != null)
            {
                Member = (MemberInfo)CachedMember;
            }
            else
            {
                MemberInfo[] Members = ObjType.GetMember(MethodName, BindingType | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (Members.Length > 0)
                    Member = Members[0];
                else
                {
                    // If we can't find any suitable instance members, try to find them as statics - but we only want to allow implicit static
                    // lookups for fields/properties/events -kevinh
                    Members = ObjType.GetMember(MethodName, BindingType | BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);

                    if (Members.Length > 0)
                    {
                        Member = Members[0];
                        ImplicitStatic = true;
                    }
                }
            }
            if (Member != null)
            {
                if (Member.MemberType == MemberTypes.Field)
                {
                    FieldInfo field = (FieldInfo)Member;
                    if (CachedMember == null) SetMemberCache(MemberCache, ObjType, MethodName, Member);

                    try
                    {
                        Translator.Push(LuaState, field.GetValue(Obj));
                    }
                    catch
                    {
                        LuaCore.LuaPushNil(LuaState);
                    }
                }
                else if (Member.MemberType == MemberTypes.Property)
                {
                    PropertyInfo Property = (PropertyInfo)Member;
                    if (CachedMember == null) SetMemberCache(MemberCache, ObjType, MethodName, Member);

                    try
                    {
                        object Val = Property.GetValue(Obj, null);

                        Translator.Push(LuaState, Val);
                    }
                    catch (ArgumentException)
                    {
                        // If we can't find the getter in our class, recurse up to the base class and see
                        // if they can help.

                        if (ObjType is Type && !(((Type)ObjType) == typeof(object)))
                            return GetMember(LuaState, ((Type)ObjType).BaseType, Obj, MethodName, BindingType);
                        else
                            LuaCore.LuaPushNil(LuaState);
                    }
                    catch (TargetInvocationException e)  // Convert this exception into a Lua error
                    {
                        ThrowError(LuaState, e);
                        LuaCore.LuaPushNil(LuaState);
                    }
                }
                else if (Member.MemberType == MemberTypes.Event)
                {
                    EventInfo EventInfo = (EventInfo)Member;
                    if (CachedMember == null) SetMemberCache(MemberCache, ObjType, MethodName, Member);
                    Translator.Push(LuaState, new RegisterEventHandler(Translator.PendingEvents, Obj, EventInfo));
                }
                else if (!ImplicitStatic)
                {
                    if (Member.MemberType == MemberTypes.NestedType)
                    {
                        // kevinh - added support for finding nested types

                        // cache us
                        if (CachedMember == null) SetMemberCache(MemberCache, ObjType, MethodName, Member);

                        // Find the name of our class
                        string Name = Member.Name;
                        Type DecType = Member.DeclaringType;

                        // Build a new long name and try to find the type by name
                        string Longname = DecType.FullName + "+" + Name;
                        Type NestedType = Translator.FindType(Longname);

                        Translator.PushType(LuaState, NestedType);
                    }
                    else
                    {
                        // Member type must be 'method'
                        KopiLua.LuaNativeFunction Wrapper = new KopiLua.LuaNativeFunction((new LuaMethodWrapper(Translator, ObjType, MethodName, BindingType)).Call);

                        if (CachedMember == null) SetMemberCache(MemberCache, ObjType, MethodName, Wrapper);
                        Translator.PushFunction(LuaState, Wrapper);
                        Translator.Push(LuaState, true);
                        return 2;
                    }
                }
                else
                {
                    // If we reach this point we found a static method, but can't use it in this context because the user passed in an instance
                    Translator.ThrowError(LuaState, "can't pass instance to static method " + MethodName);

                    LuaCore.LuaPushNil(LuaState);
                }
            }
            else
            {
                // kevinh - we want to throw an exception because meerly returning 'nil' in this case
                // is not sufficient.  valid data members may return nil and therefore there must be some
                // way to know the member just doesn't exist.

                Translator.ThrowError(LuaState, "unknown member name " + MethodName);

                LuaCore.LuaPushNil(LuaState);
            }

            // push false because we are NOT returning a function (see luaIndexFunction)
            Translator.Push(LuaState, false);
            return 2;
        }
        
        /// <summary>
        /// __newindex metafunction of CLR objects. Receives the object,
        /// the member name and the value to be stored as arguments.
        /// Throws an error if the assignment is invalid.
        /// </summary>
        private int SetFieldOrProperty(KopiLua.LuaState LuaState)
        {
            object Target = Translator.GetRawNetObject(LuaState, 1);
            if (Target == null)
            {
                Translator.ThrowError(LuaState, "trying to index and invalid object reference");
                return 0;
            }
            Type ObjType = Target.GetType();

            // First try to look up the parameter as a property name
            string DetailMessage;
            bool DidMember = TrySetMember(LuaState, ObjType, Target, BindingFlags.Instance | BindingFlags.IgnoreCase, out DetailMessage);

            if (DidMember)
            {
                return 0;       // Must have found the property name
            }

            // We didn't find a property name, now see if we can use a [] style this accessor to set array contents
            try
            {
                if (ObjType.IsArray && LuaCore.LuaIsNumber(LuaState, 2))
                {
                    int index = (int)LuaCore.LuaToNumber(LuaState, 2);

                    Array arr = (Array)Target;
                    object val = Translator.GetAsType(LuaState, 3, arr.GetType().GetElementType());
                    arr.SetValue(val, index);
                }
                else
                {
                    // Try to see if we have a this[] accessor
                    MethodInfo Setter = ObjType.GetMethod("set_Item");
                    if (Setter != null)
                    {
                        ParameterInfo[] Args = Setter.GetParameters();
                        Type ValueType = Args[1].ParameterType;

                        // The new val ue the user specified
                        object Val = Translator.GetAsType(LuaState, 3, ValueType);

                        Type IndexType = Args[0].ParameterType;
                        object Index = Translator.GetAsType(LuaState, 2, IndexType);

                        object[] MethodArgs = new object[2];

                        // Just call the indexer - if out of bounds an exception will happen
                        MethodArgs[0] = Index;
                        MethodArgs[1] = Val;

                        Setter.Invoke(Target, MethodArgs);
                    }
                    else
                    {
                        Translator.ThrowError(LuaState, DetailMessage); // Pass the original message from trySetMember because it is probably best
                    }
                }
            }
            catch (SEHException)
            {
                // If we are seeing a C++ exception - this must actually be for Lua's private use.  Let it handle it
                throw;
            }
            catch (Exception e)
            {
                ThrowError(LuaState, e);
            }
            return 0;
        }

        /// <summary>
        /// __index metafunction of type references, works on static members.
        /// </summary>
        private int GetClassMethod(KopiLua.LuaState LuaState)
        {
            IReflect Klass;
            object Obj = Translator.GetRawNetObject(LuaState, 1);
            if (Obj == null || !(Obj is IReflect))
            {
                Translator.ThrowError(LuaState, "trying to index an invalid type reference");
                LuaCore.LuaPushNil(LuaState);
                return 1;
            }
            else
            {
                Klass = (IReflect)Obj;
            }
            if (LuaCore.LuaIsNumber(LuaState, 2))
            {
                int Size = (int)LuaCore.LuaToNumber(LuaState, 2);
                Translator.Push(LuaState, Array.CreateInstance(Klass.UnderlyingSystemType, Size));
                return 1;
            }
            else
            {
                string MethodName = LuaCore.LuaToString(LuaState, 2);
                if (MethodName == null)
                {
                    LuaCore.LuaPushNil(LuaState);
                    return 1;
                } //CP: Ignore case
                
                return GetMember(LuaState, Klass, null, MethodName, BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.IgnoreCase);
            }
        }
        
        /// <summary>
        /// __newindex function of type references, works on static members.
        /// </summary>
        private int SetClassFieldOrProperty(KopiLua.LuaState LuaState)
        {
            IReflect Target;
            object Obj = Translator.GetRawNetObject(LuaState, 1);
            if (Obj == null || !(Obj is IReflect))
            {
                Translator.ThrowError(LuaState, "trying to index an invalid type reference");
                return 0;
            }
            else
            {
                Target = (IReflect)Obj; 
            }
            return SetMember(LuaState, Target, null, BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.IgnoreCase);
        }

        /// <summary>
        /// __call metafunction of type references. Searches for and calls
        /// a constructor for the type.
        /// Throws an error if the constructor generates an exception.
        /// </summary>
        /// <param name="LuaState"></param>
        /// <returns>Nil if constuctor is not found of if the aguments are invalid</returns>
        private int CallConstructor(KopiLua.LuaState LuaState)
        {
            MethodCache ValidConstructor = new MethodCache();
            IReflect Klass;
            object Obj = Translator.GetRawNetObject(LuaState, 1);

            if (Obj == null || !(Obj is IReflect))
            {
                Translator.ThrowError(LuaState, "trying to call constructor on an invalid type reference");
                LuaCore.LuaPushNil(LuaState);
                return 1;
            }
            else
            {
                Klass = (IReflect)Obj;
            }

            LuaCore.LuaRemove(LuaState, 1);
            ConstructorInfo[] Constructors = Klass.UnderlyingSystemType.GetConstructors();
            foreach (ConstructorInfo Constructor in Constructors)
            {
                bool IsConstructor = MatchParameters(LuaState, Constructor, ref ValidConstructor);
                if (IsConstructor)
                {
                    try
                    {
                        Translator.Push(LuaState, Constructor.Invoke(ValidConstructor.Args));
                    }
                    catch (TargetInvocationException e)
                    {
                        ThrowError(LuaState, e);
                        LuaCore.LuaPushNil(LuaState);
                    }
                    catch
                    {
                        LuaCore.LuaPushNil(LuaState);
                    }
                    return 1;
                }
            }

            string ConstructorName = (Constructors.Length == 0) ? "unknown" : Constructors[0].Name;

            Translator.ThrowError(
                LuaState, String.Format(
                    "{0} does not contain constructor({1}) argument match",
                    Klass.UnderlyingSystemType, ConstructorName
                )
            );
            LuaCore.LuaPushNil(LuaState);
            return 1;
        }
        #endregion

        #region Member Functions
        /// <summary>
        /// Checks if a MemberInfo object is cached, returning it or null.
        /// </summary>
        private object CheckMemberCache(Hashtable MemberCache, IReflect ObjType, string MemberName)
        {
            Hashtable Members = (Hashtable)MemberCache[ObjType];
            if (Members != null)
            {
                return Members[MemberName];
            }

            return null;
        }
        
        /// <summary>
        /// Stores a MemberInfo object in the member cache.
        /// </summary>
        private void SetMemberCache(Hashtable MemberCache, IReflect ObjType, string MemberName, object Member)
        {
            Hashtable Members = (Hashtable)MemberCache[ObjType];
            if (Members == null)
            {
                Members = new Hashtable();
                MemberCache[ObjType] = Members;
            }
            Members[MemberName] = Member;
        }

        /// <summary>
        /// Tries to set a named property or field
        /// </summary>
        /// <returns>false if unable to find the named member, true for success</returns>
        private bool TrySetMember(KopiLua.LuaState LuaState, IReflect TargetType, object Target, BindingFlags BindingType, out string DetailMessage)
        {
            DetailMessage = null;   // No error yet

            // If not already a string just return - we don't want to call tostring - which has the side effect of
            // changing the lua typecode to string
            // Note: We don't use isstring because the standard lua C isstring considers either strings or numbers to
            // be true for isstring.
            if (LuaCore.LuaType(LuaState, 2) != LuaTypes.LUA_TSTRING)
            {
                DetailMessage = "property names must be strings";
                return false;
            }

            // We only look up property names by string
            string FieldName = LuaCore.LuaToString(LuaState, 2);
            if (FieldName == null || FieldName.Length < 1 || !(char.IsLetter(FieldName[0]) || FieldName[0] == '_'))
            {
                DetailMessage = "invalid property name";
                return false;
            }

            // Find our member via reflection or the cache
            MemberInfo Member = (MemberInfo)CheckMemberCache(MemberCache, TargetType, FieldName);
            if (Member == null)
            {
                MemberInfo[] Members = TargetType.GetMember(FieldName, BindingType | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (Members.Length > 0)
                {
                    Member = Members[0];
                    SetMemberCache(MemberCache, TargetType, FieldName, Member);
                }
                else
                {
                    DetailMessage = "field or property '" + FieldName + "' does not exist";
                    return false;
                }
            }

            if (Member.MemberType == MemberTypes.Field)
            {
                FieldInfo Field = (FieldInfo)Member;
                object Val = Translator.GetAsType(LuaState, 3, Field.FieldType);
                try
                {
                    Field.SetValue(Target, Val);
                }
                catch (Exception e)
                {
                    ThrowError(LuaState, e);
                }
                // We did a call
                return true;
            }
            else if (Member.MemberType == MemberTypes.Property)
            {
                PropertyInfo Property = (PropertyInfo)Member;
                object Val = Translator.GetAsType(LuaState, 3, Property.PropertyType);
                try
                {
                    Property.SetValue(Target, Val, null);
                }
                catch (Exception e)
                {
                    ThrowError(LuaState, e);
                }
                // We did a call
                return true;
            }

            DetailMessage = "'" + FieldName + "' is not a .net field or property";
            return false;
        }

        /// <summary>
        /// Writes to fields or properties, either static or instance. 
        /// Throws an error if the operation is invalid.
        /// </summary>
        private int SetMember(KopiLua.LuaState LuaState, IReflect TargetType, object Target, BindingFlags BindingType)
        {
            string Detail;
            bool Success = TrySetMember(LuaState, TargetType, Target, BindingType, out Detail);

            if (!Success) Translator.ThrowError(LuaState, Detail);

            return 0;
        }

        /// <summary>
        /// Does this method exist as either an instance or static?
        /// </summary>
        bool IsMemberPresent(IReflect ObjType, string MethodName)
        {
            object CachedMember = CheckMemberCache(MemberCache, ObjType, MethodName);

            if (CachedMember != null) return true;

            MemberInfo[] Members = ObjType.GetMember(MethodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return (Members.Length > 0);
        }
        #endregion

        #region Utilities
        /// <summary>
        /// Convert a C# exception into a Lua error
        /// </summary>
        /// <param name="e"></param>
        /// We try to look into the exception to give the most meaningful description
        void ThrowError(KopiLua.LuaState LuaState, Exception e)
        {
            // If we got inside a reflection show what really happened
            TargetInvocationException Te = e as TargetInvocationException;

            if (Te != null) e = Te.InnerException;

            Translator.ThrowError(LuaState, e);
        }

        private static bool IsInteger(double x)
        {
            return Math.Ceiling(x) == x;
        }

        internal Array TableToArray(object LuaParamValue, Type ParamArrayType)
        {
            Array ParamArray;

            if (LuaParamValue is LuaTable)
            {
                LuaTable Table = (LuaTable)LuaParamValue;
                IDictionaryEnumerator TableEnumerator = Table.GetEnumerator();
                TableEnumerator.Reset();
                ParamArray = Array.CreateInstance(ParamArrayType, Table.Values.Count);

                int ParamArrayIndex = 0;

                while (TableEnumerator.MoveNext())
                {
                    object Obj = TableEnumerator.Value;
                    if (ParamArrayType == typeof(object))
                    {
                        if (Obj != null && Obj.GetType() == typeof(double) && IsInteger((double)Obj))
                        {
                            Obj = Convert.ToInt32((double)Obj);
                        }
                    }

                    ParamArray.SetValue(Convert.ChangeType(Obj, ParamArrayType), ParamArrayIndex);
                    ParamArrayIndex++;
                }
            }
            else
            {
                ParamArray = Array.CreateInstance(ParamArrayType, 1);
                ParamArray.SetValue(LuaParamValue, 0);
            }

            return ParamArray;
        }
        #endregion

        #region Method checking
        /// <summary>
        /// Matches a method against its arguments in the Lua stack.
        /// It also returns the information necessary to invoke the method.
        /// </summary>
        /// <returns>If the match was successful</returns>
        internal bool MatchParameters(KopiLua.LuaState LuaState, MethodBase Method, ref MethodCache MethodCache)
        {
            ExtractValue ExtractValue;
            bool IsMethod = true;
            ParameterInfo[] ParamInfo = Method.GetParameters();
            int CurrentLuaParam = 1;
            int NLuaParams = LuaCore.LuaGetTop(LuaState);
            ArrayList ParamList = new ArrayList();
            List<int> OutList = new List<int>();
            List<MethodArgs> ArgTypes = new List<MethodArgs>();
            foreach (ParameterInfo CurrentNetParam in ParamInfo)
            {
                if (!CurrentNetParam.IsIn && CurrentNetParam.IsOut)  // Skips out params
                {
                    OutList.Add(ParamList.Add(null));
                }
                else if (CurrentLuaParam > NLuaParams) // Adds optional parameters
                {
                    if (CurrentNetParam.IsOptional)
                    {
                        ParamList.Add(CurrentNetParam.DefaultValue);
                    }
                    else
                    {
                        IsMethod = false;
                        break;
                    }
                }
                else if (_IsTypeCorrect(LuaState, CurrentLuaParam, CurrentNetParam, out ExtractValue))  // Type checking
                {
                    int Index = ParamList.Add(ExtractValue(LuaState, CurrentLuaParam));

                    MethodArgs MethodArg = new MethodArgs();
                    MethodArg.Index = Index;
                    MethodArg.ExtractVal = ExtractValue;
                    ArgTypes.Add(MethodArg);

                    if (CurrentNetParam.ParameterType.IsByRef) OutList.Add(Index);
                    CurrentLuaParam++;
                } // Type does not match, ignore if the parameter is optional
                else if (_IsParamsArray(LuaState, CurrentLuaParam, CurrentNetParam, out ExtractValue))
                {
                    object LuaParamValue = ExtractValue(LuaState, CurrentLuaParam);
                    Type ParamArrayType = CurrentNetParam.ParameterType.GetElementType();

                    Array paramArray = TableToArray(LuaParamValue, ParamArrayType);
                    int Index = ParamList.Add(paramArray);

                    MethodArgs MethodArg = new MethodArgs();
                    MethodArg.Index = Index;
                    MethodArg.ExtractVal = ExtractValue;
                    MethodArg.IsParamsArray = true;
                    MethodArg.ParamsArrayType = ParamArrayType;
                    ArgTypes.Add(MethodArg);

                    CurrentLuaParam++;
                }
                else if (CurrentNetParam.IsOptional)
                {
                    ParamList.Add(CurrentNetParam.DefaultValue);
                }
                else  // No match
                {
                    IsMethod = false;
                    break;
                }
            }

            // Number of parameters does not match
            if (CurrentLuaParam != NLuaParams + 1)
            {
                IsMethod = false;
            }

            if (IsMethod)
            {
                MethodCache.Args = ParamList.ToArray();
                MethodCache.CachedMethod = Method;
                MethodCache.OutList = OutList.ToArray();
                MethodCache.ArgTypes = ArgTypes.ToArray();
            }
            return IsMethod;
        }

        /// <summary>
        /// Fix for operator overloading failure
        /// </summary>
        /// <returns>True if the type is set</returns>
        private bool _IsTypeCorrect(KopiLua.LuaState LuaState, int CurrentLuaParam, ParameterInfo CurrentNetParam, out ExtractValue ExtractValue)
        {
            try
            {
                return (ExtractValue = Translator.TypeChecker._CheckType(LuaState, CurrentLuaParam, CurrentNetParam.ParameterType)) != null;
            }
            catch
            {
                ExtractValue = null;
                return false;
            }
        }

        private bool _IsParamsArray(KopiLua.LuaState LuaState, int CurrentLuaParam, ParameterInfo CurrentNetParam, out ExtractValue ExtractValue)
        {
            ExtractValue = null;

            if (CurrentNetParam.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
            {
                LuaTypes LuaType;

                try
                {
                    LuaType = LuaCore.LuaType(LuaState, CurrentLuaParam);
                }
                catch (Exception Ex)
                {
                    ExtractValue = null;
                    return false;
                }

                if (LuaType == LuaTypes.LUA_TTABLE)
                {
                    try
                    {
                        ExtractValue = Translator.TypeChecker.GetExtractor(typeof(LuaTable));
                    }
                    catch (Exception ex) { }

                    if (ExtractValue != null)
                    {
                        return true;
                    }
                }
                else
                {
                    Type ParamElementType = CurrentNetParam.ParameterType.GetElementType();

                    try
                    {
                        ExtractValue = Translator.TypeChecker._CheckType(LuaState, CurrentLuaParam, ParamElementType);
                    }
                    catch (Exception ex) { }

                    if (ExtractValue != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        #endregion
    }
}
