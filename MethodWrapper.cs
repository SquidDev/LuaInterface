using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace LuaInterface
{

    /// <summary>
    /// Cached method
    /// </summary>
    public struct MethodCache
    {
        private MethodBase _CachedMethod;

        public MethodBase CachedMethod
        {
            get
            {
                return _CachedMethod;
            }
            set
            {
                _CachedMethod = value;
                MethodInfo MInfo = value as MethodInfo;
                if (MInfo != null)
                {
                    //SJD this is guaranteed to be correct irrespective of actual name used for type..
					IsReturnVoid = MInfo.ReturnType == typeof(void);
                }
            }
        }

        public bool IsReturnVoid;

        /// <summary>
        /// List or arguments
        /// </summary>
        public object[] Args;

        /// <summary>
        /// Positions of out parameters
        /// </summary>
        public int[] OutList;
        // 
        /// <summary>
        /// Types of parameters
        /// </summary>
        public MethodArgs[] ArgTypes;
    }

    /// <summary>
    ///  Parameter information
    /// </summary>
    public struct MethodArgs
    {
        /// <summary>
        /// Position of parameter
        /// </summary>
        public int Index;
        
        /// <summary>
        /// Type-conversion function
        /// </summary>
        public ExtractValue ExtractVal;

        public bool IsParamsArray;

        public Type ParamsArrayType;
    }

    /// <summary>
    /// Argument extraction with type-conversion function
    /// </summary>
    public delegate object ExtractValue(KopiLua.LuaState LuaState, int StackPos);

    /// <summary>
    /// Wrapper class for methods/constructors accessed from Lua.
    /// </summary>
    public class LuaMethodWrapper
    {
        private ObjectTranslator _Translator;
        private MethodBase _Method;
        private MethodCache _LastCalledMethod = new MethodCache();
        private string _MethodName;
        private MemberInfo[] _Members;
        private IReflect _TargetType;
        private ExtractValue _ExtractTarget;
        private object _Target;
        private BindingFlags _BindingType;

        /// <summary>
        /// Constructs the wrapper for a known MethodBase instance
        /// </summary>
        public LuaMethodWrapper(ObjectTranslator Translator, object Target, IReflect TargetType, MethodBase Method)
        {
            _Translator = Translator;
            _Target = Target;
            _TargetType = TargetType;
            if (TargetType != null)
            {
                _ExtractTarget = Translator.TypeChecker.GetExtractor(TargetType);
            }
            _Method = Method;
            _MethodName = Method.Name;

            if (Method.IsStatic)
            {
                _BindingType = BindingFlags.Static; 
            }
            else
            {
                _BindingType = BindingFlags.Instance;
            }
        }
        
        /// <summary>
        /// Constructs the wrapper for a known method name
        /// </summary>
        public LuaMethodWrapper(ObjectTranslator Translator, IReflect TargetType, string MethodName, BindingFlags BindingType)
        {
            _Translator = Translator;
            _MethodName = MethodName;
            _TargetType = TargetType;

            if (TargetType != null)
            {
                _ExtractTarget = Translator.TypeChecker.GetExtractor(TargetType);
            }

            _BindingType = BindingType;

            _Members = TargetType.UnderlyingSystemType.GetMember(MethodName, MemberTypes.Method, BindingType | BindingFlags.Public | BindingFlags.IgnoreCase);
        }


        /// <summary>
        /// Convert C# exceptions into Lua errors
        /// </summary>
        /// <returns>num of things on stack</returns>
        /// <param name="e">null for no pending exception</param>
        int SetPendingException(Exception e)
        {
            return _Translator.Interpreter.SetPendingException(e);
        }
		
		private static bool IsInteger(double x) {
			return Math.Ceiling(x) == x;	
		}			

        /// <summary>
        /// Calls the method. Receives the arguments from the Lua stack and returns value in it
        /// </summary>
        /// <param name="LuaState"></param>
        /// <returns></returns>
        public int Call(KopiLua.LuaState LuaState)
        {
            MethodBase MethodToCall = _Method;
            object TargetObject = _Target;
            bool FailedCall = true;
            int NReturnValues = 0;

            if (!LuaCore.LuaCheckStack(LuaState, 5))
            {
                throw new LuaException("Lua stack overflow");
            }

            bool IsStatic = (_BindingType & BindingFlags.Static) == BindingFlags.Static;

            SetPendingException(null);

            if (MethodToCall == null) // Method from name
            {
                if (IsStatic)
                {
                    TargetObject = null;
                }
                else
                {
                    TargetObject = _ExtractTarget(LuaState, 1);
                }

                if (_LastCalledMethod.CachedMethod != null) // Cached?
                {
                    int NumStackToSkip = IsStatic ? 0 : 1; // If this is an instance invoe we will have an extra arg on the stack for the targetObject
                    int NumArgsPassed = LuaCore.LuaGetTop(LuaState) - NumStackToSkip;					
    				MethodBase Method = _LastCalledMethod.CachedMethod;
					
					if (NumArgsPassed == _LastCalledMethod.ArgTypes.Length) // No. of args match?
                    {
                        if (!LuaCore.LuaCheckStack(LuaState, _LastCalledMethod.OutList.Length + 6))
                        {
                            throw new LuaException("Lua stack overflow");
                        }
						
     					object[] Args = _LastCalledMethod.Args;
						
						try
                        {
                            for (int i = 0; i < _LastCalledMethod.ArgTypes.Length; i++)
                            {
								MethodArgs MArgs = _LastCalledMethod.ArgTypes[i];
								object LuaParamValue = MArgs.ExtractVal(LuaState, i + 1 + NumStackToSkip);
                                if (_LastCalledMethod.ArgTypes[i].IsParamsArray)
                                {									                                    
                                    Args[MArgs.Index] = _Translator.TableToArray(LuaParamValue,MArgs.ParamsArrayType);
                                }
                                else
                                {
                                    Args[MArgs.Index] = LuaParamValue;
                                }

                                if (Args[MArgs.Index] == null && !LuaCore.LuaIsNil(LuaState, i + 1 + NumStackToSkip))
                                {
                                    throw new LuaException("argument number " + (i + 1) + " is invalid");
                                }
                            }
                            if ((_BindingType & BindingFlags.Static) == BindingFlags.Static)
                            {
                                _Translator.Push(LuaState, Method.Invoke(null, Args));
                            }
                            else
                            {
                                if (_LastCalledMethod.CachedMethod.IsConstructor)
                                {
                                    _Translator.Push(LuaState, ((ConstructorInfo)Method).Invoke(Args));
                                }
                                else
                                {
                                    _Translator.Push(LuaState, Method.Invoke(TargetObject, Args));
                                }
                            }
                            FailedCall = false;
                        }
                        catch (TargetInvocationException e)
                        {
                            // Failure of method invocation
                            return SetPendingException(e.GetBaseException());
                        }
                        catch (Exception e)
                        {
                            if (_Members.Length == 1) // Is the method overloaded?
                                // No, throw error
                                return SetPendingException(e);
                        }
                    }
                }

                // Cache miss
                if (FailedCall)
                {
                    // If we are running an instance variable, we can now pop the targetObject from the stack
                    if (!IsStatic)
                    {
                        if (TargetObject == null)
                        {
                            _Translator.ThrowError(LuaState, String.Format("instance method '{0}' requires a non null target object", _MethodName));
                            LuaCore.LuaPushNil(LuaState);
                            return 1;
                        }

                        LuaCore.LuaRemove(LuaState, 1); // Pops the receiver
                    }

                    bool HasMatch = false;
                    string CandidateName = null;

                    foreach (MemberInfo Member in _Members)
                    {
                        CandidateName = Member.ReflectedType.Name + "." + Member.Name;

                        MethodBase MBase = (MethodInfo)Member;

                        bool IsMethod = _Translator.MatchParameters(LuaState, MBase, ref _LastCalledMethod);
                        if (IsMethod)
                        {
                            HasMatch = true;
                            break;
                        }
                    }
                    if (!HasMatch)
                    {
                        string Msg = (CandidateName == null)
                            ? "invalid arguments to method call"
                            : ("invalid arguments to method: " + CandidateName);

                        _Translator.ThrowError(LuaState, Msg);
                        LuaCore.LuaPushNil(LuaState);
                        return 1;
                    }
                }
            }
            else // Method from MethodBase instance
            {
                if (MethodToCall.ContainsGenericParameters)
                {
                    // bool isMethod = //* not used
                    _Translator.MatchParameters(LuaState, MethodToCall, ref _LastCalledMethod);

                    if (MethodToCall.IsGenericMethodDefinition)
                    {
                        //need to make a concrete type of the generic method definition
                        List<Type> TypeArgs = new List<Type>();

                        foreach (object Arg in _LastCalledMethod.Args)
                        {
                            TypeArgs.Add(Arg.GetType());
                        }

                        MethodInfo ConcreteMethod = (MethodToCall as MethodInfo).MakeGenericMethod(TypeArgs.ToArray());

                        _Translator.Push(LuaState, ConcreteMethod.Invoke(TargetObject, _LastCalledMethod.Args));
                        FailedCall = false;
                    }
                    else if (MethodToCall.ContainsGenericParameters)
                    {
                        _Translator.ThrowError(LuaState, "unable to invoke method on generic class as the current method is an open generic method");
                        LuaCore.LuaPushNil(LuaState);
                        return 1;
                    }
                }
                else
                {
                    if (!MethodToCall.IsStatic && !MethodToCall.IsConstructor && TargetObject == null)
                    {
                        TargetObject = _ExtractTarget(LuaState, 1);
                        LuaCore.LuaRemove(LuaState, 1); // Pops the receiver
                    }

                    if (!_Translator.MatchParameters(LuaState, MethodToCall, ref _LastCalledMethod))
                    {
                        _Translator.ThrowError(LuaState, "invalid arguments to method call");
                        LuaCore.LuaPushNil(LuaState);
                        return 1;
                    }
                }
            }

            if (FailedCall)
            {
                if (!LuaCore.LuaCheckStack(LuaState, _LastCalledMethod.OutList.Length + 6))
                {
                    throw new LuaException("Lua stack overflow");
                }
                try
                {
                    if (IsStatic)
                    {
                        _Translator.Push(LuaState, _LastCalledMethod.CachedMethod.Invoke(null, _LastCalledMethod.Args));
                    }
                    else
                    {
                        if (_LastCalledMethod.CachedMethod.IsConstructor)
                        {
                            _Translator.Push(LuaState, ((ConstructorInfo)_LastCalledMethod.CachedMethod).Invoke(_LastCalledMethod.Args));
                        }
                        else
                        {
                            object Return = _LastCalledMethod.CachedMethod.Invoke(TargetObject, _LastCalledMethod.Args);
                            if (Return != null && Return.GetType().IsArray)
                            {
                                //If is an array then return each element.
                                foreach (object Obj in ((IEnumerable)Return))
                                {
                                    _Translator.Push(LuaState, Obj);
                                    NReturnValues++;
                                }
                                //Need to minus one element as don't want to return whole array as well.
                                NReturnValues--;
                            }
                            else
                            {
                                _Translator.Push(LuaState, Return);
                            }
                            
                        }
                    }
                }
                catch (TargetInvocationException e)
                {
                    return SetPendingException(e.GetBaseException());
                }
                catch (Exception e)
                {
                    return SetPendingException(e);
                }
            }

            // Pushes out and ref return values
            for (int index = 0; index < _LastCalledMethod.OutList.Length; index++)
            {
                NReturnValues++;
                _Translator.Push(LuaState, _LastCalledMethod.Args[_LastCalledMethod.OutList[index]]);
            }

            //by isSingle 2010-09-10 11:26:31
            //Desc:
            //  if not return void,we need add 1,
            //  or we will lost the function's return value
            //  when call dotnet function like "int foo(arg1,out arg2,out arg3)" in lua code
            if (!_LastCalledMethod.IsReturnVoid && NReturnValues > 0)
            {
                NReturnValues++;
            }

            return NReturnValues < 1 ? 1 : NReturnValues;
        }
    }


    /// <summary>
    /// We keep track of what delegates we have auto attached to an event - to allow us to cleanly exit a CCLib.LuaInterface session
    /// </summary>
    public class EventHandlerContainer : IDisposable
    {
        Dictionary<Delegate, RegisterEventHandler> Dict = new Dictionary<Delegate, RegisterEventHandler>();

        public void Add(Delegate Handler, RegisterEventHandler EventInfo)
        {
            Dict.Add(Handler, EventInfo);
        }

        public void Remove(Delegate Handler)
        {
            bool Found = Dict.Remove(Handler);
            Debug.Assert(Found);
        }

        /// <summary>
        /// Remove any still registered handlers
        /// </summary>
        public void Dispose()
        {
            foreach (KeyValuePair<Delegate, RegisterEventHandler> Pair in Dict)
            {
                Pair.Value.RemovePending(Pair.Key);
            }

            Dict.Clear();
        }
    }

    /// <summary>
    /// Wrapper class for events that does registration/deregistration of event handlers.
    /// </summary>
    public class RegisterEventHandler
    {
        object Target;
        EventInfo EventInfo;
        EventHandlerContainer PendingEvents;

        public RegisterEventHandler(EventHandlerContainer PendingEvents, object Target, EventInfo EventInfo)
        {
            this.Target = Target;
            this.EventInfo = EventInfo;
            this.PendingEvents = PendingEvents;
        }

        /// <summary>
        /// Adds a new event handler
        /// </summary>
        public Delegate Add(LuaFunction Function)
        {
            //CP: Fix by Ben Bryant for event handling with one parameter
            //link: http://luaforge.net/forum/message.php?msg_id=9266

            Delegate HandlerDelegate = CodeGeneration.Instance.GetDelegate(EventInfo.EventHandlerType, Function);
            EventInfo.AddEventHandler(Target, HandlerDelegate);
            PendingEvents.Add(HandlerDelegate, this);

            return HandlerDelegate;
        }

        /// <summary>
        /// Removes an existing event handler
        /// </summary>
        public void Remove(Delegate HandlerDelegate)
        {
            RemovePending(HandlerDelegate);
            PendingEvents.Remove(HandlerDelegate);
        }

        /// <summary>
        /// Removes an existing event handler (without updating the pending handlers list)
        /// </summary>
        internal void RemovePending(Delegate HandlerDelegate)
        {
            EventInfo.RemoveEventHandler(Target, HandlerDelegate);
        }
    }

    /// <summary>
    /// Base wrapper class for Lua function event handlers.
    /// Subclasses that do actual event handling are created at runtime.
    /// </summary>
    public class LuaEventHandler
    {
        public LuaFunction Handler = null;

        // CP: Fix provided by Ben Bryant for delegates with one param
        // link: http://luaforge.net/forum/message.php?msg_id=9318
        public void HandleEvent(object[] Args)
        {
            Handler.Call(Args);
        }
    }

    /// <summary>
    /// Wrapper class for Lua functions as delegates
    /// Subclasses with correct signatures are created at runtime.
    /// </summary>
    public class LuaDelegate
    {
        public Type[] ReturnTypes;
        public LuaFunction Function;
        public LuaDelegate()
        {
            Function = null;
            ReturnTypes = null;
        }

        /// <summary>
        /// Calls the function
        /// </summary>
        /// <param name="Args">Return array of arguments</param>
        /// <param name="InArgs">Array of arguments passed to the function</param>
        /// <param name="OutArgs">The positions of out parameters</param>
        public object CallFunction(object[] Args, object[] InArgs, int[] OutArgs)
        {
            object ReturnValue;
            int IRefArgs;
            object[] ReturnValues = Function.Call(InArgs, ReturnTypes);
            if (ReturnTypes[0] == typeof(void))
            {
                ReturnValue = null;
                IRefArgs = 0;
            }
            else
            {
                ReturnValue = ReturnValues[0];
                IRefArgs = 1;
            }
            // Sets the value of out and ref parameters (from
            // the values returned by the Lua function).
            for (int i = 0; i < OutArgs.Length; i++)
            {
                Args[OutArgs[i]] = ReturnValues[IRefArgs];
                IRefArgs++;
            }

            return ReturnValue;
        }
    }

    /// <summary>
    /// Static helper methods for Lua tables acting as CLR objects.
    /// </summary>
    public class LuaClassHelper
    {
        /// <summary>
        /// Gets the function called Name from the provided Table
        /// </summary>
        /// <param name="Table">The table to search</param>
        /// <param name="Name">The function name</param>
        /// <returns>Null if function does not exist</returns>
        public static LuaFunction GetTableFunction(LuaTable Table, string Name)
        {
            object FuncObj = Table.RawGet(Name);
            if (FuncObj is LuaFunction) return (LuaFunction)FuncObj;
            
            return null;
        }
        
        /// <summary>
        /// Calls the provided function with the provided parameters
        /// </summary>
        /// <param name="Args">Return array of arguments</param>
        /// <param name="InArgs">Array of arguments passed to the function</param>
        /// <param name="OutArgs">The positions of out parameters</param>
        public static object CallFunction(LuaFunction Function, object[] Args, Type[] ReturnTypes, object[] InArgs, int[] OutArgs)
        {
            object ReturnValue;
            int IRefArgs;
            object[] ReturnValues = Function.Call(InArgs, ReturnTypes);
            if (ReturnTypes[0] == typeof(void))
            {
                ReturnValue = null;
                IRefArgs = 0;
            }
            else
            {
                ReturnValue = ReturnValues[0];
                IRefArgs = 1;
            }
            for (int i = 0; i < OutArgs.Length; i++)
            {
                Args[OutArgs[i]] = ReturnValues[IRefArgs];
                IRefArgs++;
            }
            return ReturnValue;
        }
    }
}
