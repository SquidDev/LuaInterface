using System;
using System.Collections;
using System.Collections.Generic;

namespace LuaInterface
{
    /// <summary>
    /// Wrapper class for Lua tables
    /// </summary>
    public class LuaTable : LuaBase
    {
        protected IDictionary Data;

        public LuaTable(int Reference, Lua Interpreter)
        {
            _Reference = Reference;
            _Interpreter = Interpreter;
        }

        public LuaTable(Lua Interpreter)
            : this(0, Interpreter)
        {
            Data = new Dictionary<object, object>();
        }

        public LuaTable(IDictionary Items, Lua Interpreter)
            : this(0, Interpreter)
        {
            Data = Items;
        }

        public LuaTable(IEnumerable Items, Lua Interpreter)
            : this(0, Interpreter)
        {
            int I = 0;
            Data = new Dictionary<object, object>();

            foreach (object Item in Items)
            {
                I++;
                Data.Add(I, Item);
            }
        }

        #region Indexers
        public object this[string Field]
        {
            get
            {
                if (_Reference != 0)
                {
                    return _Interpreter.GetObject(_Reference, Field);
                }
                else
                {
                    return Data[Field];
                }
            }
            set
            {
                if (_Reference != 0)
                {
                    _Interpreter.SetObject(_Reference, Field, value);
                }
                else
                {
                    Data[Field] = value;
                }
            }
        }

        /// <summary>
        /// Indexer for numeric fields of the table
        /// </summary>
        public object this[object Field]
        {
            get
            {
                if (_Reference != 0)
                {
                    return _Interpreter.GetObject(_Reference, Field);
                }
                else
                {
                    return Data[Field];
                }
            }
            set
            {
                if (_Reference != 0)
                {
                    _Interpreter.SetObject(_Reference, Field, value);
                }
                else
                {
                    Data[Field] = value;
                }
            }
        }
        #endregion

        #region IDictionary Methods
        public System.Collections.IDictionaryEnumerator GetEnumerator()
        {
            if (_Reference != 0)
            {
                return _Interpreter.GetTableDict(this).GetEnumerator();
            }
            else
            {
                return Data.GetEnumerator();
            }
        }

        public ICollection Keys
        {
            get
            {
                if (_Reference != 0)
                {
                    return _Interpreter.GetTableDict(this).Keys;
                }
                else
                {
                    return Data.Keys;
                }
            }
        }

        public ICollection Values
        {
            get
            {
                if (_Reference != 0)
                {
                    return _Interpreter.GetTableDict(this).Values;
                }
                else
                {
                    return Data.Values;
                }
            }
        }
        #endregion

        /// <summary>
        /// Gets an string fields of a table ignoring its metatable, if it exists.
        /// </summary>
        internal object RawGet(string Field)
        {
            if (_Reference != 0)
            {
                return _Interpreter.RawGetObject(_Reference, Field);
            }
            else
            {
                return _Interpreter[Field];
            }
            
        }

        internal object RawGetFunction(string Field)
        {
            object Obj = RawGet(Field);

            if (Obj is KopiLua.LuaNativeFunction)
            {
                return new LuaFunction((KopiLua.LuaNativeFunction)Obj, _Interpreter);
            }
            else
            {
                return Obj;
            }
        }

        /// <summary>
        /// Pushes this table into the Lua stack
        /// </summary>
        public override void Push(KopiLua.LuaState LuaState, ObjectTranslator Translator = null)
        {
            if (_Reference != 0)
            {
                LuaCore.LuaGetRef(LuaState, _Reference);
            }
            else
            {
                LuaCore.LuaNewTable(LuaState);
                foreach (DictionaryEntry Item in Data)
                {
                    _Interpreter.Push(Item.Key);
                    _Interpreter.Push(Item.Value);
                    LuaCore.LuaSetTable(LuaState, -3);
                }
            }
        }

        public override string ToString()
        {
            return String.Format("table ({0})", GetHashCode());
        }
    }
}
