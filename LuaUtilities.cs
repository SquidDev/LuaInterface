using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LuaInterface
{
    public class LuaUtilities
    {
        /// <summary>
        /// Debug tool to dump the lua stack
        /// </summary>
        /// FIXME, move somewhere else
        public static void DumpStack(ObjectTranslator Translator, KopiLua.LuaState LuaState)
        {
            int Depth = LuaCore.LuaGetTop(LuaState);

            Debug.WriteLine("Lua stack depth: " + Depth);
            for (int i = 1; i <= Depth; i++)
            {
                LuaTypes LType = LuaCore.LuaType(LuaState, i);
                // we dump stacks when deep in calls, calling typename while the stack is in flux can fail sometimes, so manually check for key types
                string TypeStr = (LType == LuaTypes.LUA_TTABLE) ? "table" : LuaCore.LuaTypeName(LuaState, LType);

                string StrRep = LuaCore.LuaToString(LuaState, i);
                if (LType == LuaTypes.LUA_TUSERDATA)
                {
                    object Obj = Translator.GetRawNetObject(LuaState, i);
                    StrRep = Obj.ToString();
                }

                Debug.Print("{0}: ({1}) {2}", i, TypeStr, StrRep);
            }
        }
    }
}
