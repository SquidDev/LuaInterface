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
            this.Reference = Reference;
            this.LuaInstance = Interpreter;
        }

        public LuaTable(Lua Interpreter)
            : this(0, Interpreter = null)
        {
            Data = new Dictionary<object, object>();
        }

        public LuaTable(IDictionary Items, Lua Interpreter = null)
            : this(0, Interpreter)
        {
            Data = Items;
        }

        public LuaTable(IEnumerable Items, Lua Interpreter = null)
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
                if (Reference != 0)
                {
                    return LuaInstance.GetObject(Reference, Field);
                }
                else
                {
                    return Data[Field];
                }
            }
            set
            {
                if (Reference != 0)
                {
                    LuaInstance.SetObject(Reference, Field, value);
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
                if (Reference != 0)
                {
                    return LuaInstance.GetObject(Reference, Field);
                }
                else
                {
                    return Data[Field];
                }
            }
            set
            {
                if (Reference != 0)
                {
                    LuaInstance.SetObject(Reference, Field, value);
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
            if (Reference != 0)
            {
                return LuaInstance.GetTableDict(this).GetEnumerator();
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
                if (Reference != 0)
                {
                    return LuaInstance.GetTableDict(this).Keys;
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
                if (Reference != 0)
                {
                    return LuaInstance.GetTableDict(this).Values;
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
            if (Reference != 0)
            {
                return LuaInstance.RawGetObject(Reference, Field);
            }
            else
            {
                return LuaInstance[Field];
            }
            
        }

        internal object RawGetFunction(string Field)
        {
            object Obj = RawGet(Field);

            if (Obj is KopiLua.LuaNativeFunction)
            {
                return new LuaFunction((KopiLua.LuaNativeFunction)Obj, LuaInstance);
            }
            else
            {
                return Obj;
            }
        }

        /// <summary>
        /// Pushes this table into the Lua stack
        /// </summary>
        public override void Push(KopiLua.LuaState LuaState, ObjectTranslator Translator)
        {
            if (Reference != 0)
            {
                LuaCore.LuaGetRef(LuaState, Reference);
            }
            else
            {
                LuaCore.LuaNewTable(LuaState);
                foreach (DictionaryEntry Item in Data)
                {
                    Translator.Push(LuaState, Item.Key);
                    Translator.Push(LuaState, Item.Value);
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
