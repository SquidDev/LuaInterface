using System;
using System.Collections.Generic;
using System.Reflection;

namespace LuaInterface
{
    /// <summary>
    /// Type checking and conversion functions.
    /// </summary>
    class CheckType
    {
        private ObjectTranslator Translator;

        ExtractValue ExtractNetObject;
        Dictionary<long, ExtractValue> ExtractValues;

        public CheckType(ObjectTranslator Translator)
        {
            this.Translator = Translator;

            ExtractValues = new Dictionary<long, ExtractValue>(){
                {typeof(object).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsObject)},
                {typeof(sbyte).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsSbyte)},
                {typeof(byte).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsByte)},
                {typeof(short).TypeHandle.Value.ToInt64(), new ExtractValue(getAsShort)},
                {typeof(ushort).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUshort)},
                {typeof(int).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsInt)},
                {typeof(uint).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUint)},
                {typeof(long).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsLong)},
                {typeof(ulong).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUlong)},
                {typeof(double).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsDouble)},
                {typeof(char).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsChar)},
                {typeof(float).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsFloat)},
                {typeof(decimal).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsDecimal)},
                {typeof(bool).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsBoolean)},
                {typeof(string).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsString)},
                {typeof(LuaFunction).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsFunction)},
                {typeof(LuaTable).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsTable)},
                {typeof(LuaUserData).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUserdata)},
            };

            ExtractNetObject = new ExtractValue(GetAsNetObject);
        }

        #region Get Extractors
        /// <summary>
        /// Checks if the value at Lua stack index StackPos matches ParamType, 
        /// </summary>
        /// <param name="ParamType">The Parameter Type</param>
        /// <returns>Conversion function or null</returns>
        internal ExtractValue GetExtractor(IReflect ParamType)
        {
            return GetExtractor(ParamType.UnderlyingSystemType);
        }

        /// <summary>
        /// Checks if the value at Lua stack index StackPos matches ParamType, 
        /// </summary>
        /// <param name="ParamType">The Parameter Type</param>
        /// <returns>Conversion function or null</returns>
        internal ExtractValue GetExtractor(Type ParamType)
        {
            if(ParamType.IsByRef) ParamType=ParamType.GetElementType();

            long RuntimeHandleValue = ParamType.TypeHandle.Value.ToInt64();

            if(ExtractValues.ContainsKey(RuntimeHandleValue))
            {
                return ExtractValues[RuntimeHandleValue];
            }
            return ExtractNetObject;
        }
        #endregion

        internal ExtractValue _CheckType(KopiLua.LuaState State, int StackPos, Type ParamType)
        {
            LuaTypes LuaType = LuaCore.LuaType(State, StackPos);

            if(ParamType.IsByRef) ParamType=ParamType.GetElementType();

            Type UnderlyingType = Nullable.GetUnderlyingType(ParamType);
            if (UnderlyingType != null)
            {
                // Silently convert nullable types to their non null requics
                ParamType = UnderlyingType;
            }

            long RuntimeHandleValue = ParamType.TypeHandle.Value.ToInt64();

            if (ParamType.Equals(typeof(object)))
            { 
                return ExtractValues[RuntimeHandleValue];
            }

            //CP: Added support for generic parameters
            if (ParamType.IsGenericParameter)
            {
                switch (LuaType)
                {
                    case LuaTypes.LUA_TBOOLEAN:
                        return ExtractValues[typeof(bool).TypeHandle.Value.ToInt64()];
                    case LuaTypes.LUA_TSTRING:
                        return ExtractValues[typeof(string).TypeHandle.Value.ToInt64()];
                    case LuaTypes.LUA_TTABLE:
                        return ExtractValues[typeof(LuaTable).TypeHandle.Value.ToInt64()];
                    case LuaTypes.LUA_TUSERDATA:
                        return ExtractValues[typeof(object).TypeHandle.Value.ToInt64()];
                    case LuaTypes.LUA_TFUNCTION:
                        return ExtractValues[typeof(LuaFunction).TypeHandle.Value.ToInt64()];
                    case LuaTypes.LUA_TNUMBER:
                        return ExtractValues[typeof(double).TypeHandle.Value.ToInt64()];
                }
            }

            if (LuaCore.LuaIsNumber(State, StackPos))
            {
                return ExtractValues[RuntimeHandleValue];
            }
                
            if (ParamType == typeof(bool))
            {
                if (LuaCore.LuaIsBoolean(State, StackPos))
                {
                    return ExtractValues[RuntimeHandleValue];
                }
            }
            else if (ParamType == typeof(string))
            {
                if (LuaCore.LuaIsString(State, StackPos))
                {
                    return ExtractValues[RuntimeHandleValue];
                }
                else if (LuaType == LuaTypes.LUA_TNIL)
                {
                    // kevinh - silently convert nil to a null string pointer
                    return ExtractNetObject; 
                }
                    
            }
            else if (ParamType == typeof(LuaTable))
            {
                if (LuaType == LuaTypes.LUA_TTABLE)
                {
                    return ExtractValues[RuntimeHandleValue];
                }
            }
            else if (ParamType == typeof(LuaUserData))
            {
                if (LuaType == LuaTypes.LUA_TUSERDATA)
                {
                    return ExtractValues[RuntimeHandleValue];
                }
            }
            else if (ParamType == typeof(LuaFunction))
            {
                if (LuaType == LuaTypes.LUA_TFUNCTION)
                {
                    return ExtractValues[RuntimeHandleValue];
                }
            }
            else if (typeof(Delegate).IsAssignableFrom(ParamType) && LuaType == LuaTypes.LUA_TFUNCTION)
            {
                return new ExtractValue(new DelegateGenerator(Translator, ParamType).ExtractGenerated);
            }
            else if (ParamType.IsInterface && LuaType == LuaTypes.LUA_TTABLE)
            {
                return new ExtractValue(new ClassGenerator(Translator, ParamType).ExtractGenerated);
            }
            else if ((ParamType.IsInterface || ParamType.IsClass) && LuaType == LuaTypes.LUA_TNIL)
            {
                // kevinh - allow nil to be silently converted to null
                //extractNetObject will return null when the item isn't found
                return ExtractNetObject;
            }
            else if (LuaCore.LuaType(State, StackPos) == LuaTypes.LUA_TTABLE)
            {
                if (LuaCore.LuaLGetMetafield(State, StackPos, "__index"))
                {
                    object obj = Translator.GetNetObject(State, -1);
                    LuaCore.LuaSetTop(State, -2);
                    if (obj != null && ParamType.IsAssignableFrom(obj.GetType()))
                    {
                        return ExtractNetObject;
                    }
                }
                return null;
            }
            else
            {
                object obj = Translator.GetNetObject(State, StackPos);
                if (obj != null && ParamType.IsAssignableFrom(obj.GetType()))
                {
                    return ExtractNetObject;
                }
            }

            return null;
        }

        /*
         * Return the value in the Lua stack index StackPos as the desired type 
         * if it can, or null otherwise.
         */
        #region GetAs
        private object GetAsSbyte(KopiLua.LuaState LuaState, int StackPos)
        {
            sbyte RetVal = (sbyte)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsByte(KopiLua.LuaState LuaState, int StackPos)
        {
            byte RetVal = (byte)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object getAsShort(KopiLua.LuaState LuaState, int StackPos)
        {
            short RetVal = (short)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsUshort(KopiLua.LuaState LuaState, int StackPos)
        {
            ushort RetVal = (ushort)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsInt(KopiLua.LuaState LuaState, int StackPos)
        {
            int RetVal = (int)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsUint(KopiLua.LuaState LuaState, int StackPos)
        {
            uint RetVal = (uint)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsLong(KopiLua.LuaState LuaState, int StackPos)
        {
            long RetVal = (long)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsUlong(KopiLua.LuaState LuaState, int StackPos)
        {
            ulong RetVal = (ulong)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsDouble(KopiLua.LuaState LuaState, int StackPos)
        {
            double RetVal=LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsChar(KopiLua.LuaState LuaState, int StackPos)
        {
            char RetVal = (char)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsFloat(KopiLua.LuaState LuaState, int StackPos)
        {
            float RetVal = (float)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsDecimal(KopiLua.LuaState LuaState, int StackPos)
        {
            decimal RetVal = (decimal)LuaCore.LuaToNumber(LuaState, StackPos);
            if(RetVal == 0 && !LuaCore.LuaIsNumber(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsBoolean(KopiLua.LuaState LuaState, int StackPos)
        {
            return LuaCore.LuaToBoolean(LuaState, StackPos);
        }
        private object GetAsString(KopiLua.LuaState LuaState, int StackPos)
        {
            string RetVal=LuaCore.LuaToString(LuaState, StackPos);
            if(RetVal=="" && !LuaCore.LuaIsString(LuaState, StackPos)) return null;
            return RetVal;
        }
        private object GetAsTable(KopiLua.LuaState LuaState, int StackPos)
        {
            return Translator.GetTable(LuaState, StackPos);
        }
        private object GetAsFunction(KopiLua.LuaState LuaState, int StackPos)
        {
            return Translator.getFunction(LuaState, StackPos);
        }
        private object GetAsUserdata(KopiLua.LuaState LuaState, int StackPos)
        {
            return Translator.getUserData(LuaState, StackPos);
        }
        public object GetAsObject(KopiLua.LuaState LuaState, int StackPos)
        {
            if(LuaCore.LuaType(LuaState, StackPos)==LuaTypes.LUA_TTABLE)
            {
                if(LuaCore.LuaLGetMetafield(LuaState, StackPos, "__index"))
                {
                    if(LuaCore.LuaLCheckMetatable(LuaState, 1))
                    {
                        LuaCore.LuaInsert(LuaState, StackPos);
                        LuaCore.LuaRemove(LuaState, StackPos+1);
                    }
                    else
                    {
                        LuaCore.LuaSetTop(LuaState, 2);
                    }
                }
            }
            return Translator.GetObject(LuaState, StackPos);
        }
        public object GetAsNetObject(KopiLua.LuaState LuaState, int StackPos)
        {
            object Obj = Translator.GetNetObject(LuaState, StackPos);
            if(Obj == null && LuaCore.LuaType(LuaState, StackPos) == LuaTypes.LUA_TTABLE)
            {
                if(LuaCore.LuaLGetMetafield(LuaState, StackPos, "__index"))
                {
                    if(LuaCore.LuaLCheckMetatable(LuaState, -1))
                    {
                        LuaCore.LuaInsert(LuaState, StackPos);
                        LuaCore.LuaRemove(LuaState, StackPos + 1);
                        Obj = Translator.GetNetObject(LuaState, StackPos);
                    }
                    else
                    {
                        LuaCore.LuaSetTop(LuaState, -2);
                    }
                }
            }
            return Obj;
        }
        #endregion
    }
}
