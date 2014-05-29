using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace LuaInterface
{
    /// <summary>
    /// Structure to store a type and the return types of its methods
    /// (The type of the returned value and out/ref paremters)
    /// </summary>
    struct LuaClassType
    {
        public Type Klass;
        public Type[][] ReturnTypes;
    }

    /// <summary>
    /// Common interface for types generated from tables. The method
    /// returns the table that overrides some or all of the type's methods.
    /// </summary>
    public interface ILuaGeneratedType
    {
        LuaTable __LuaInterface_getLuaTable();
    }

    /// <summary>
    /// Used for generating delegates that get a function from the lua stack
    /// as a delegate of a specific type
    /// </summary>
    class DelegateGenerator
    {
        private ObjectTranslator Translator;
        private Type DelegateType;

        public DelegateGenerator(ObjectTranslator Translator, Type DelegateType)
        {
            this.Translator = Translator;
            this.DelegateType = DelegateType;
        }
        public object ExtractGenerated(KopiLua.LuaState LuaState, int StackPos)
        {
            return CodeGeneration.Instance.GetDelegate(DelegateType, Translator.getFunction(LuaState, StackPos));
        }
    }

    /// <summary>
    /// Generates delegates that get a table from the lua stack as an object of a specific type
    /// </summary>
    class ClassGenerator
    {
        private ObjectTranslator Translator;
        private Type Klass;

        public ClassGenerator(ObjectTranslator Translator, Type Klass)
        {
            this.Translator = Translator;
            this.Klass = Klass;
        }
        public object ExtractGenerated(KopiLua.LuaState LuaState, int StackPos)
        {
            return CodeGeneration.Instance.GetClassInstance(Klass, Translator.GetTable(LuaState, StackPos));
        }
    }

    /// <summary>
    /// Dynamically generates new types from existing types and
    /// Lua function and table values. Generated types are event handlers,
    /// delegates, interface implementations and subclasses.
    /// </summary>
    class CodeGeneration
    {
        private Type EventHandlerParent = typeof(LuaEventHandler);
        private Dictionary<Type, Type> EventHandlerCollection = new Dictionary<Type, Type>();

        private Type DelegateParent = typeof(LuaDelegate);
        private Dictionary<Type, Type> DelegateCollection = new Dictionary<Type, Type>();

        private Type ClassHelper = typeof(LuaClassHelper);
        private Dictionary<Type, LuaClassType> ClassCollection = new Dictionary<Type, LuaClassType>();

        private AssemblyName _AssemblyName;
        private AssemblyBuilder NewAssembly;
        private ModuleBuilder NewModule;
        private int LuaClassNumber = 1;
        private static readonly CodeGeneration _Instance = new CodeGeneration();

        private CodeGeneration()
        {
            // Create an assembly name
            _AssemblyName = new AssemblyName();
            _AssemblyName.Name = "CCLib.LuaInterface_generatedcode";
            // Create a new assembly with one module.
            NewAssembly = Thread.GetDomain().DefineDynamicAssembly(
                _AssemblyName, AssemblyBuilderAccess.Run);
            NewModule = NewAssembly.DefineDynamicModule("CCLib.LuaInterface_generatedcode");
        }

        /// <summary>
        /// Singleton instance of the class
        /// </summary>
        public static CodeGeneration Instance
        {
            get
            {
                return _Instance;
            }
        }
        #region Generate
        /// <summary>
        /// Generates an event handler that calls a Lua function
        /// </summary>
        /// <param name="EventHandlerType">Type of the event handler</param>
        /// <returns></returns>
        private Type GenerateEvent(Type EventHandlerType)
        {
            string TypeName;
            lock (this)
            {
                TypeName = "LuaGeneratedClass" + LuaClassNumber;
                LuaClassNumber++;
            }
            // Define a public class in the assembly, called typeName
            TypeBuilder MyType = NewModule.DefineType(TypeName, TypeAttributes.Public, EventHandlerParent);

            // Defines the handler method. Its signature is void(object,<subclassofEventArgs>)
            Type[] ParamTypes = new Type[2];
            ParamTypes[0] = typeof(object);
            ParamTypes[1] = EventHandlerType;

            Type ReturnType = typeof(void);
            MethodBuilder HandleMethod = MyType.DefineMethod(
                "HandleEvent", MethodAttributes.Public | MethodAttributes.HideBySig,
                ReturnType, ParamTypes
            );

            // Emits the IL for the method. It loads the arguments
            // and calls the handleEvent method of the base class
            ILGenerator Generator = HandleMethod.GetILGenerator();
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldarg_1);
            Generator.Emit(OpCodes.Ldarg_2);

            MethodInfo MiGenericEventHandler = EventHandlerParent.GetMethod("handleEvent");
            Generator.Emit(OpCodes.Call, MiGenericEventHandler);
            // returns
            Generator.Emit(OpCodes.Ret);

            // creates the new type
            return MyType.CreateType();
        }

        /// <summary>
        /// Generates a type that can be used for instantiating a delegate
        /// of the provided type, given a Lua function.
        /// </summary>
        /// <param name="DelegateType">The type of the delegate</param>
        /// <returns></returns>
        private Type GenerateDelegate(Type DelegateType)
        {
            string TypeName;
            lock (this)
            {
                TypeName = "LuaGeneratedClass" + LuaClassNumber;
                LuaClassNumber++;
            }
            // Define a public class in the assembly, called typeName
            TypeBuilder MyType = NewModule.DefineType(TypeName, TypeAttributes.Public, DelegateParent);

            // Defines the delegate method with the same signature as the
            // Invoke method of delegateType
            MethodInfo InvokeMethod = DelegateType.GetMethod("Invoke");
            ParameterInfo[] ParamInfo = InvokeMethod.GetParameters();
            Type[] ParamTypes = new Type[ParamInfo.Length];
            Type ReturnType = InvokeMethod.ReturnType;

            // Counts out and ref params, for use later
            int NOutParams = 0; int NOutAndRefParams = 0;
            for (int i = 0; i < ParamTypes.Length; i++)
            {
                ParamTypes[i] = ParamInfo[i].ParameterType;
                if ((!ParamInfo[i].IsIn) && ParamInfo[i].IsOut)
                {
                    NOutParams++;
                }
                if (ParamTypes[i].IsByRef)
                {
                    NOutAndRefParams++;
                }
            }
            int[] RefArgs = new int[NOutAndRefParams];

            MethodBuilder DelegateMethod = MyType.DefineMethod("CallFunction", InvokeMethod.Attributes, ReturnType, ParamTypes);

            // Generates the IL for the method
            ILGenerator Generator = DelegateMethod.GetILGenerator();

            Generator.DeclareLocal(typeof(object[])); // original arguments
            Generator.DeclareLocal(typeof(object[])); // with out-only arguments removed
            Generator.DeclareLocal(typeof(int[])); // indexes of out and ref arguments
            if (!(ReturnType == typeof(void))){
                Generator.DeclareLocal(ReturnType);
            }
            else{
                Generator.DeclareLocal(typeof(object));
            }
                
            // Initializes local variables
            Generator.Emit(OpCodes.Ldc_I4, ParamTypes.Length);
            Generator.Emit(OpCodes.Newarr, typeof(object));
            Generator.Emit(OpCodes.Stloc_0);
            Generator.Emit(OpCodes.Ldc_I4, ParamTypes.Length - NOutParams);
            Generator.Emit(OpCodes.Newarr, typeof(object));
            Generator.Emit(OpCodes.Stloc_1);
            Generator.Emit(OpCodes.Ldc_I4, NOutAndRefParams);
            Generator.Emit(OpCodes.Newarr, typeof(int));
            Generator.Emit(OpCodes.Stloc_2);
            // Stores the arguments in the local variables
            for (int iArgs = 0, iInArgs = 0, iOutArgs = 0; iArgs < ParamTypes.Length; iArgs++)
            {
                Generator.Emit(OpCodes.Ldloc_0);
                Generator.Emit(OpCodes.Ldc_I4, iArgs);
                Generator.Emit(OpCodes.Ldarg, iArgs + 1);
                if (ParamTypes[iArgs].IsByRef)
                {
                    if (ParamTypes[iArgs].GetElementType().IsValueType)
                    {
                        Generator.Emit(OpCodes.Ldobj, ParamTypes[iArgs].GetElementType());
                        Generator.Emit(OpCodes.Box, ParamTypes[iArgs].GetElementType());
                    }
                    else
                    {
                        Generator.Emit(OpCodes.Ldind_Ref);
                    }
                }
                else
                {
                    if (ParamTypes[iArgs].IsValueType)
                    {
                        Generator.Emit(OpCodes.Box, ParamTypes[iArgs]);
                    }
                }
                Generator.Emit(OpCodes.Stelem_Ref);
                if (ParamTypes[iArgs].IsByRef)
                {
                    Generator.Emit(OpCodes.Ldloc_2);
                    Generator.Emit(OpCodes.Ldc_I4, iOutArgs);
                    Generator.Emit(OpCodes.Ldc_I4, iArgs);
                    Generator.Emit(OpCodes.Stelem_I4);
                    RefArgs[iOutArgs] = iArgs;
                    iOutArgs++;
                }
                if (ParamInfo[iArgs].IsIn || (!ParamInfo[iArgs].IsOut))
                {
                    Generator.Emit(OpCodes.Ldloc_1);
                    Generator.Emit(OpCodes.Ldc_I4, iInArgs);
                    Generator.Emit(OpCodes.Ldarg, iArgs + 1);
                    if (ParamTypes[iArgs].IsByRef)
                    {
                        if (ParamTypes[iArgs].GetElementType().IsValueType)
                        {
                            Generator.Emit(OpCodes.Ldobj, ParamTypes[iArgs].GetElementType());
                            Generator.Emit(OpCodes.Box, ParamTypes[iArgs].GetElementType());
                        }
                        else Generator.Emit(OpCodes.Ldind_Ref);
                    }
                    else
                    {
                        if (ParamTypes[iArgs].IsValueType)
                            Generator.Emit(OpCodes.Box, ParamTypes[iArgs]);
                    }
                    Generator.Emit(OpCodes.Stelem_Ref);
                    iInArgs++;
                }
            }
            // Calls the callFunction method of the base class
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldloc_0);
            Generator.Emit(OpCodes.Ldloc_1);
            Generator.Emit(OpCodes.Ldloc_2);
            MethodInfo MiGenericEventHandler;
            MiGenericEventHandler = DelegateParent.GetMethod("callFunction");
            Generator.Emit(OpCodes.Call, MiGenericEventHandler);
            // Stores return value
            if (ReturnType == typeof(void))
            {
                Generator.Emit(OpCodes.Pop);
                Generator.Emit(OpCodes.Ldnull);
            }
            else if (ReturnType.IsValueType)
            {
                Generator.Emit(OpCodes.Unbox, ReturnType);
                Generator.Emit(OpCodes.Ldobj, ReturnType);
            }
            else
            {
                Generator.Emit(OpCodes.Castclass, ReturnType);
            }
            Generator.Emit(OpCodes.Stloc_3);
            // Stores new value of out and ref params
            for (int i = 0; i < RefArgs.Length; i++)
            {
                Generator.Emit(OpCodes.Ldarg, RefArgs[i] + 1);
                Generator.Emit(OpCodes.Ldloc_0);
                Generator.Emit(OpCodes.Ldc_I4, RefArgs[i]);
                Generator.Emit(OpCodes.Ldelem_Ref);
                if (ParamTypes[RefArgs[i]].GetElementType().IsValueType)
                {
                    Generator.Emit(OpCodes.Unbox, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Ldobj, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Stobj, ParamTypes[RefArgs[i]].GetElementType());
                }
                else
                {
                    Generator.Emit(OpCodes.Castclass, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Stind_Ref);
                }
            }
            // Returns
            if (!(ReturnType == typeof(void)))
            {
                Generator.Emit(OpCodes.Ldloc_3);
            }
            Generator.Emit(OpCodes.Ret);

            // creates the new type
            return MyType.CreateType();
        }

        /// <summary>
        /// Generates an implementation of Klass, if it is an interface, or
        /// a subclass of Klass that delegates its virtual methods to a Lua table.
        /// </summary>
        public void GenerateClass(Type Klass, out Type NewType, out Type[][] ReturnTypes)
        {
            string TypeName;
            lock (this)
            {
                TypeName = "LuaGeneratedClass" + LuaClassNumber;
                LuaClassNumber++;
            }
            TypeBuilder MyType;
            // Define a public class in the assembly, called typeName
            if (Klass.IsInterface)
            {
                MyType = NewModule.DefineType(TypeName, TypeAttributes.Public, typeof(object), new Type[] { Klass, typeof(ILuaGeneratedType) });
            }
            else
            {
                MyType = NewModule.DefineType(TypeName, TypeAttributes.Public, Klass, new Type[] { typeof(ILuaGeneratedType) });
            }

            // Field that stores the Lua table
            FieldBuilder LuaTableField = MyType.DefineField("__LuaInterface_luaTable", typeof(LuaTable), FieldAttributes.Public);

            // Field that stores the return types array
            FieldBuilder ReturnTypesField = MyType.DefineField("__LuaInterface_returnTypes", typeof(Type[][]), FieldAttributes.Public);

            // Generates the constructor for the new type, it takes a Lua table and an array
            // of return types and stores them in the respective fields
            ConstructorBuilder Constructor = MyType.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, 
                new Type[] { typeof(LuaTable), typeof(Type[][]) }
            );
            ILGenerator Generator = Constructor.GetILGenerator();
            Generator.Emit(OpCodes.Ldarg_0);

            if (Klass.IsInterface)
            {
                Generator.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
            }
            else
            {
                Generator.Emit(OpCodes.Call, Klass.GetConstructor(Type.EmptyTypes));
            }

            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldarg_1);
            Generator.Emit(OpCodes.Stfld, LuaTableField);
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldarg_2);
            Generator.Emit(OpCodes.Stfld, ReturnTypesField);
            Generator.Emit(OpCodes.Ret);

            // Generates overriden versions of the klass' public virtual methods
            MethodInfo[] ClassMethods = Klass.GetMethods();
            ReturnTypes = new Type[ClassMethods.Length][];
            int i = 0;
            foreach (MethodInfo Method in ClassMethods)
            {
                if (Klass.IsInterface)
                {
                    GenerateMethod(
                        MyType, Method,
                        MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                        i, LuaTableField, ReturnTypesField, false, out ReturnTypes[i]
                    );
                    i++;
                }
                else
                {
                    if (!Method.IsPrivate && !Method.IsFinal && Method.IsVirtual)
                    {
                        GenerateMethod(
                            MyType, Method,
                            (Method.Attributes | MethodAttributes.NewSlot) ^ MethodAttributes.NewSlot,
                            i, LuaTableField, ReturnTypesField, true, out ReturnTypes[i]
                        );
                        i++;
                    }
                }
            }
            // Generates an implementation of the __LuaInterface_getLuaTable method
            MethodBuilder ReturnTableMethod = MyType.DefineMethod(
                "__LuaInterface_getLuaTable",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                typeof(LuaTable), new Type[0]
            );

            MyType.DefineMethodOverride(ReturnTableMethod, typeof(ILuaGeneratedType).GetMethod("__LuaInterface_getLuaTable"));
            Generator = ReturnTableMethod.GetILGenerator();
            Generator.Emit(OpCodes.Ldfld, LuaTableField);
            Generator.Emit(OpCodes.Ret);
            // Creates the type
            NewType = MyType.CreateType();
        }

        /// <summary>
        /// Generates an overriden implementation of method inside myType that delegates
        /// to a function in a Lua table with the same name, if the function exists. If it
        /// doesn't the method calls the base method (or does nothing, in case of interface
        /// implementations).
        /// </summary>
        private void GenerateMethod(
            TypeBuilder MyType, MethodInfo Method, MethodAttributes Attributes,
            int MethodIndex, FieldInfo LuaTableField, FieldInfo ReturnTypesField, 
            bool GenerateBase, out Type[] ReturnTypes
        )
        {
            ParameterInfo[] ParamInfo = Method.GetParameters();
            Type[] ParamTypes = new Type[ParamInfo.Length];
            List<Type> ReturnTypesList = new List<Type>();

            // Counts out and ref parameters, for later use,
            // and creates the list of return types
            int NOutParams = 0; 
            int NOutAndRefParams = 0;

            Type ReturnType = Method.ReturnType;
            ReturnTypesList.Add(ReturnType);

            for (int i = 0; i < ParamTypes.Length; i++)
            {
                ParamTypes[i] = ParamInfo[i].ParameterType;
                if ((!ParamInfo[i].IsIn) && ParamInfo[i].IsOut)
                {
                    NOutParams++;
                }
                if (ParamTypes[i].IsByRef)
                {
                    ReturnTypesList.Add(ParamTypes[i].GetElementType());
                    NOutAndRefParams++;
                }
            }
            int[] RefArgs = new int[NOutAndRefParams];
            ReturnTypes = ReturnTypesList.ToArray();

            // Generates a version of the method that calls the base implementation
            // directly, for use by the base field of the table
            if (GenerateBase)
            {
                MethodBuilder BaseMethod = MyType.DefineMethod(
                    "__LuaInterface_base_" + Method.Name,
                    MethodAttributes.Private | MethodAttributes.NewSlot | MethodAttributes.HideBySig,
                    ReturnType, ParamTypes
                );
                ILGenerator GeneratorBase = BaseMethod.GetILGenerator();

                GeneratorBase.Emit(OpCodes.Ldarg_0);
                for (int i = 0; i < ParamTypes.Length; i++)
                {
                    GeneratorBase.Emit(OpCodes.Ldarg, i + 1);
                }
                GeneratorBase.Emit(OpCodes.Call, Method);
                if (ReturnType == typeof(void))
                {
                    GeneratorBase.Emit(OpCodes.Pop);
                }
                GeneratorBase.Emit(OpCodes.Ret);
            }

            // Defines the method
            MethodBuilder MethodImpl = MyType.DefineMethod(Method.Name, Attributes, ReturnType, ParamTypes);

            // If it's an implementation of an interface tells what method it
            // is overriding
            if (MyType.BaseType.Equals(typeof(object)))
            {
                MyType.DefineMethodOverride(MethodImpl, Method);
            }

            ILGenerator Generator = MethodImpl.GetILGenerator();

            Generator.DeclareLocal(typeof(object[])); // original arguments
            Generator.DeclareLocal(typeof(object[])); // with out-only arguments removed
            Generator.DeclareLocal(typeof(int[])); // indexes of out and ref arguments
            if (!(ReturnType == typeof(void)))
            {
                Generator.DeclareLocal(ReturnType);
            }
            else
            {
                Generator.DeclareLocal(typeof(object));
            }

            // Initializes local variables
            Generator.Emit(OpCodes.Ldc_I4, ParamTypes.Length);
            Generator.Emit(OpCodes.Newarr, typeof(object));
            Generator.Emit(OpCodes.Stloc_0);
            Generator.Emit(OpCodes.Ldc_I4, ParamTypes.Length - NOutParams + 1);
            Generator.Emit(OpCodes.Newarr, typeof(object));
            Generator.Emit(OpCodes.Stloc_1);
            Generator.Emit(OpCodes.Ldc_I4, NOutAndRefParams);
            Generator.Emit(OpCodes.Newarr, typeof(int));
            Generator.Emit(OpCodes.Stloc_2);
            Generator.Emit(OpCodes.Ldloc_1);
            Generator.Emit(OpCodes.Ldc_I4_0);
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldfld, LuaTableField);
            Generator.Emit(OpCodes.Stelem_Ref);

            // Stores the arguments into the local variables, as needed
            for (int iArgs = 0, iInArgs = 1, iOutArgs = 0; iArgs < ParamTypes.Length; iArgs++)
            {
                Generator.Emit(OpCodes.Ldloc_0);
                Generator.Emit(OpCodes.Ldc_I4, iArgs);
                Generator.Emit(OpCodes.Ldarg, iArgs + 1);
                if (ParamTypes[iArgs].IsByRef)
                {
                    if (ParamTypes[iArgs].GetElementType().IsValueType)
                    {
                        Generator.Emit(OpCodes.Ldobj, ParamTypes[iArgs].GetElementType());
                        Generator.Emit(OpCodes.Box, ParamTypes[iArgs].GetElementType());
                    }
                    else
                    {
                        Generator.Emit(OpCodes.Ldind_Ref);
                    }
                }
                else
                {
                    if (ParamTypes[iArgs].IsValueType)
                    {
                        Generator.Emit(OpCodes.Box, ParamTypes[iArgs]);
                    }
                }
                Generator.Emit(OpCodes.Stelem_Ref);
                if (ParamTypes[iArgs].IsByRef)
                {
                    Generator.Emit(OpCodes.Ldloc_2);
                    Generator.Emit(OpCodes.Ldc_I4, iOutArgs);
                    Generator.Emit(OpCodes.Ldc_I4, iArgs);
                    Generator.Emit(OpCodes.Stelem_I4);
                    RefArgs[iOutArgs] = iArgs;
                    iOutArgs++;
                }
                if (ParamInfo[iArgs].IsIn || (!ParamInfo[iArgs].IsOut))
                {
                    Generator.Emit(OpCodes.Ldloc_1);
                    Generator.Emit(OpCodes.Ldc_I4, iInArgs);
                    Generator.Emit(OpCodes.Ldarg, iArgs + 1);
                    if (ParamTypes[iArgs].IsByRef)
                    {
                        if (ParamTypes[iArgs].GetElementType().IsValueType)
                        {
                            Generator.Emit(OpCodes.Ldobj, ParamTypes[iArgs].GetElementType());
                            Generator.Emit(OpCodes.Box, ParamTypes[iArgs].GetElementType());
                        }
                        else
                        {
                            Generator.Emit(OpCodes.Ldind_Ref);
                        }
                    }
                    else
                    {
                        if (ParamTypes[iArgs].IsValueType)
                            Generator.Emit(OpCodes.Box, ParamTypes[iArgs]);
                    }
                    Generator.Emit(OpCodes.Stelem_Ref);
                    iInArgs++;
                }
            }

            // Gets the function the method will delegate to by calling
            // the getTableFunction method of class LuaClassHelper
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldfld, LuaTableField);
            Generator.Emit(OpCodes.Ldstr, Method.Name);
            Generator.Emit(OpCodes.Call, ClassHelper.GetMethod("getTableFunction"));
            Label Label1 = Generator.DefineLabel();
            Generator.Emit(OpCodes.Dup);
            Generator.Emit(OpCodes.Brtrue_S, Label1);

            // Function does not exist, call base method
            Generator.Emit(OpCodes.Pop);
            if (!Method.IsAbstract)
            {
                Generator.Emit(OpCodes.Ldarg_0);
                for (int i = 0; i < ParamTypes.Length; i++)
                {
                    Generator.Emit(OpCodes.Ldarg, i + 1);
                }
                Generator.Emit(OpCodes.Call, Method);
                if (ReturnType == typeof(void))
                {
                    Generator.Emit(OpCodes.Pop);
                }
                Generator.Emit(OpCodes.Ret);
                Generator.Emit(OpCodes.Ldnull);
            }
            else
            {
                Generator.Emit(OpCodes.Ldnull);
            }

            Label Label2 = Generator.DefineLabel();
            Generator.Emit(OpCodes.Br_S, Label2);
            Generator.MarkLabel(Label1);

            // Function exists, call using method callFunction of LuaClassHelper
            Generator.Emit(OpCodes.Ldloc_0);
            Generator.Emit(OpCodes.Ldarg_0);
            Generator.Emit(OpCodes.Ldfld, ReturnTypesField);
            Generator.Emit(OpCodes.Ldc_I4, MethodIndex);
            Generator.Emit(OpCodes.Ldelem_Ref);
            Generator.Emit(OpCodes.Ldloc_1);
            Generator.Emit(OpCodes.Ldloc_2);
            Generator.Emit(OpCodes.Call, ClassHelper.GetMethod("callFunction"));
            Generator.MarkLabel(Label2);

            // Stores the function return value
            if (ReturnType == typeof(void))
            {
                Generator.Emit(OpCodes.Pop);
                Generator.Emit(OpCodes.Ldnull);
            }
            else if (ReturnType.IsValueType)
            {
                Generator.Emit(OpCodes.Unbox, ReturnType);
                Generator.Emit(OpCodes.Ldobj, ReturnType);
            }
            else
            {
                Generator.Emit(OpCodes.Castclass, ReturnType);
            }
            Generator.Emit(OpCodes.Stloc_3);

            // Sets return values of out and ref parameters
            for (int i = 0; i < RefArgs.Length; i++)
            {
                Generator.Emit(OpCodes.Ldarg, RefArgs[i] + 1);
                Generator.Emit(OpCodes.Ldloc_0);
                Generator.Emit(OpCodes.Ldc_I4, RefArgs[i]);
                Generator.Emit(OpCodes.Ldelem_Ref);
                if (ParamTypes[RefArgs[i]].GetElementType().IsValueType)
                {
                    Generator.Emit(OpCodes.Unbox, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Ldobj, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Stobj, ParamTypes[RefArgs[i]].GetElementType());
                }
                else
                {
                    Generator.Emit(OpCodes.Castclass, ParamTypes[RefArgs[i]].GetElementType());
                    Generator.Emit(OpCodes.Stind_Ref);
                }
            }

            // Returns
            if (!(ReturnType == typeof(void)))
            {
                Generator.Emit(OpCodes.Ldloc_3);
            }
            Generator.Emit(OpCodes.Ret);
        }
        #endregion
        #region Get
        /// <summary>
        /// Gets an event handler for the event type that delegates to the eventHandler Lua function.
        /// Caches the generated type.
        /// </summary>
        public LuaEventHandler GetEvent(Type EventHandlerType, LuaFunction EventHandler)
        {
            Type EventConsumerType;
            if (EventHandlerCollection.ContainsKey(EventHandlerType))
            {
                EventConsumerType = EventHandlerCollection[EventHandlerType];
            }
            else
            {
                EventConsumerType = GenerateEvent(EventHandlerType);
                EventHandlerCollection[EventHandlerType] = EventConsumerType;
            }
            LuaEventHandler LuaEventHandler = (LuaEventHandler)Activator.CreateInstance(EventConsumerType);
            LuaEventHandler.Handler = EventHandler;
            return LuaEventHandler;
        }

        /// <summary>
        /// Gets a delegate with delegateType that calls the luaFunc Lua function.
        /// Caches the generated type.
        /// </summary>
        public Delegate GetDelegate(Type DelegateType, LuaFunction LuaFunc)
        {
            List<Type> ReturnTypes = new List<Type>();
            Type LuaDelegateType;
            if (DelegateCollection.ContainsKey(DelegateType))
            {
                LuaDelegateType = DelegateCollection[DelegateType];
            }
            else
            {
                LuaDelegateType = GenerateDelegate(DelegateType);
                DelegateCollection[DelegateType] = LuaDelegateType;
            }

            MethodInfo MethodInfo = DelegateType.GetMethod("Invoke");
            ReturnTypes.Add(MethodInfo.ReturnType);
            foreach (ParameterInfo ParamInfo in MethodInfo.GetParameters())
            {
                if (ParamInfo.ParameterType.IsByRef)
                {
                    ReturnTypes.Add(ParamInfo.ParameterType);
                }
            }

            LuaDelegate LuaDelegate = (LuaDelegate)Activator.CreateInstance(LuaDelegateType);
            LuaDelegate.Function = LuaFunc;
            LuaDelegate.ReturnTypes = ReturnTypes.ToArray();
            return Delegate.CreateDelegate(DelegateType, LuaDelegate, "CallFunction");
        }

        /// <summary>
        /// Gets an instance of an implementation of the klass interface or subclass of
        /// klass that delegates public virtual methods to the luaTable table.
        /// Caches the generated type.
        /// </summary>
        public object GetClassInstance(Type Klass, LuaTable LuaTable)
        {
            LuaClassType LuaClassType;
            if (ClassCollection.ContainsKey(Klass))
            {
                LuaClassType = ClassCollection[Klass];
            }
            else
            {
                LuaClassType = new LuaClassType();
                GenerateClass(Klass, out LuaClassType.Klass, out LuaClassType.ReturnTypes);
                ClassCollection[Klass] = LuaClassType;
            }
            return Activator.CreateInstance(LuaClassType.Klass, new object[] { LuaTable, LuaClassType.ReturnTypes });
        }
        #endregion
    }
}
