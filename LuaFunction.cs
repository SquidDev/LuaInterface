using System;

namespace LuaInterface
{
    public class LuaFunction : LuaBase
    {
        internal KopiLua.LuaNativeFunction Function;

        public LuaFunction(int Reference, Lua LuaInstance) : base(Reference, LuaInstance)
        {
            Function = null;
        }

        public LuaFunction(KopiLua.LuaNativeFunction Function, Lua LuaInstance) : base(0, LuaInstance)
        {
            this.Function = Function;
        }

        /// <summary>
        /// Calls the function casting return values to the types in ReturnTypes
        /// </summary>
        internal object[] Call(object[] Args, Type[] ReturnTypes)
        {
            return LuaInstance.CallFunction(this, Args, ReturnTypes);
        }
        
        /// <summary>
        /// Calls the function and returns its return values inside an array
        /// </summary>
        public object[] Call(params object[] Args)
        {
            return LuaInstance.CallFunction(this, Args);
        }

        public override void Push(KopiLua.LuaState State, ObjectTranslator Translator)
        {
            Push(State);
        }
        
        /// <summary>
        /// Pushes the function into the Lua stack
        /// </summary>
        public void Push(KopiLua.LuaState LuaState)
        {
            if (Reference != 0)
            {
                LuaCore.LuaGetRef(LuaState, Reference);
            }
            else
            {
                LuaInstance.PushCSFunction(Function);
            }
        }

        public override string ToString()
        {
            return String.Format("function ({0})",GetHashCode());
        }

        public override bool Equals(object Obj)
        {
            if (Obj is LuaFunction)
            {
                LuaFunction l = (LuaFunction)Obj;
                if (this.Reference != 0 && l.Reference != 0)
                {
                    return LuaInstance.CompareRef(l.Reference, this.Reference);
                }
                else
                {
                    return this.Function == l.Function;
                }
            }
            return false;
        }
        public override int GetHashCode()
        {
            if (Reference != 0)
            {
                return Reference;
            }
            else
            {
                return Function.GetHashCode();
            }
        }
    }

}