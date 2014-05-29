using System;
namespace LuaInterface
{
    public class LuaUserData : LuaBase
    {

        public LuaUserData(int reference, Lua interpreter)
        {
            _Reference = reference;
            _Interpreter = interpreter;
        }

        /// <summary>
        /// Indexer for string fields of the userdata
        /// </summary>
        public object this[string Field]
        {
            get
            {
                return _Interpreter.GetObject(_Reference, Field);
            }
            set
            {
                _Interpreter.SetObject(_Reference, Field, value);
            }
        }
        
        /// <summary>
        /// Indexer for numeric fields of the userdata
        /// </summary>
        public object this[object Field]
        {
            get
            {
                return _Interpreter.GetObject(_Reference, Field);
            }
            set
            {
                _Interpreter.SetObject(_Reference, Field, value);
            }
        }
        
        /// <summary>
        /// Calls the userdata and returns its return values inside an array
        /// </summary>
        public object[] Call(params object[] Args)
        {
            return _Interpreter.CallFunction(this, Args);
        }
        
        /// <summary>
        /// Pushes the userdata into the Lua stack
        /// </summary>
        internal void Push(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaGetRef(LuaState, _Reference);
        }

        public override string ToString()
        {
            return String.Format("userdata ({0})", GetHashCode());
        }
    }
}
