using System;

namespace LuaInterface
{
    public class LuaFunction : LuaBase
    {
        //private Lua interpreter;
        internal KopiLua.LuaNativeFunction Function;
        //internal int reference;

        public LuaFunction(int Reference, Lua Interpreter)
        {
            _Reference = Reference;
            this.Function = null;
            _Interpreter = Interpreter;
        }

        public LuaFunction(KopiLua.LuaNativeFunction Function, Lua Interpreter)
        {
            _Reference = 0;
            this.Function = Function;
            _Interpreter = Interpreter;
        }

        /// <summary>
        /// Calls the function casting return values to the types in ReturnTypes
        /// </summary>
        internal object[] Call(object[] Args, Type[] ReturnTypes)
        {
            return _Interpreter.CallFunction(this, Args, ReturnTypes);
        }
        
        /// <summary>
        /// Calls the function and returns its return values inside an array
        /// </summary>
        public object[] Call(params object[] Args)
        {
            return _Interpreter.CallFunction(this, Args);
        }
        
        /// <summary>
        /// Pushes the function into the Lua stack
        /// </summary>
        public void Push(KopiLua.LuaState LuaState)
        {
            if (_Reference != 0)
            {
                LuaCore.LuaGetRef(LuaState, _Reference);
            }
            else
            {
                _Interpreter.PushCSFunction(Function);
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
                if (this._Reference != 0 && l._Reference != 0)
                {
                    return _Interpreter.CompareRef(l._Reference, this._Reference);
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
            if (_Reference != 0)
            {
                return _Reference;
            }
            else
            {
                return Function.GetHashCode();
            }
        }
    }

}