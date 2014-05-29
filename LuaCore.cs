using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LuaInterface
{
    /// <summary>
    /// Lua types for the API, returned by LuaType function
    /// </summary>
    public enum LuaTypes
    {
        LUA_TNONE = -1,
        LUA_TNIL = 0,
        LUA_TNUMBER = 3,
        LUA_TSTRING = 4,
        LUA_TBOOLEAN = 1,
        LUA_TTABLE = 5,
        LUA_TFUNCTION = 6,
        LUA_TUSERDATA = 7,
        LUA_TLIGHTUSERDATA = 2
    }

    /// <summary>
    /// Lua Garbage Collector options (param "What")
    /// </summary>
    public enum LuaGCOptions
    {
        LuaGCSTOP = 0,
        LuaGCRESTART = 1,
        LuaGCCOLLECT = 2,
        LuaGCCOUNT = 3,
        LuaGCCOUNTB = 4,
        LuaGCSTEP = 5,
        LuaGCSETPAUSE = 6,
        LuaGCSETSTEPMUL = 7,
    }

    /// <summary>
    /// Lua status from thread ending / yielding
    /// </summary>
    public enum LuaThreadStatus
    {
        LUA_DEAD = 0, //Can be 0 if thread end(not part of Lua though)
        LUA_YIELD = 1,
        LUA_ERRRUN = 2,
        LUA_ERRSYNTAX = 3,
        LUA_ERRMEM = 4,
        LUA_ERRERR = 5
    }

    /// <summary>
    /// Special stack indexes
    /// </summary>
    public class LuaIndexes
    {
        public static int LUA_REGISTRYINDEX = -10000;
        public static int LUA_ENVIRONINDEX = -10001;	// steffenj: added environindex
        public static int LUA_GLOBALSINDEX = -10002;	// steffenj: globalsindex previously was -10001
    }

    /// <summary>
    /// Structure used by the chunk reader
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReaderInfo
    {
        public String ChunkData;
        public bool Finished;
    }

    /// <summary>
    /// Delegate for functions passed to Lua as function pointers
    /// </summary>
    public delegate int LuaCSFunction(KopiLua.LuaState LuaState);

    /// <summary>
    /// Delegate for chunk readers used with lua_load
    /// </summary>
    public delegate string LuaChunkReader(KopiLua.LuaState LuaState, ref ReaderInfo Data, ref uint Size);


    /// <summary>
    /// Used to handle Lua panics
    /// </summary>
    public delegate int LuaFunctionCallback(KopiLua.LuaState LuaState);

    /// <summary>
    /// P/Invoke wrapper of the Lua API
    /// </summary>
    public class LuaCore
    {
        public static int LuaGC(KopiLua.LuaState LuaState, LuaGCOptions What, int data)
        {
            return KopiLua.Lua.LuaGC(LuaState, (int)What, data);
        }

        public static string LuaTypeName(KopiLua.LuaState LuaState, LuaTypes type)
        {
            return KopiLua.Lua.LuaTypeName(LuaState, (int)type).ToString();
        }
        public static string LuaLTypeName(KopiLua.LuaState LuaState, int StackPos)
        {
            return LuaTypeName(LuaState, LuaType(LuaState, StackPos));
        }

        public static void LuaLError(KopiLua.LuaState LuaState, string Message)
        {
            KopiLua.Lua.LuaLError(LuaState, Message);
        }

        public static void LuaError(KopiLua.LuaState LuaState)
        {
            KopiLua.Lua.LuaError(LuaState);
        }

        public static KopiLua.LuaState LuaLNewState()
        {
            return KopiLua.Lua.LuaLNewState();
        }

        public static KopiLua.LuaState LuaOpen()
        {
            return LuaCore.LuaLNewState();
        }

        public static void LuaClose(KopiLua.LuaState LuaState)
        {
            KopiLua.Lua.LuaClose(LuaState);
        }

        public static void LuaLOpenLibs(KopiLua.LuaState LuaState)
        {
            KopiLua.Lua.LuaLOpenLibs(LuaState);
        }

        public static int LuaObjectLen(KopiLua.LuaState LuaState, int StackPos)
        {
            return (int)KopiLua.Lua.LuaObjectLen(LuaState, StackPos);
        }

        public static int LuaStringLen(KopiLua.LuaState LuaState, int StackPos)
        {
            return LuaObjectLen(LuaState, StackPos);
        }

        public static int LuaLLoadString(KopiLua.LuaState LuaState, string Chunk)
        {
            return KopiLua.Lua.LuaLLoadString(LuaState, Chunk);
        }

        public static int LuaLDoString(KopiLua.LuaState LuaState, string Chunk)
        {
            int result = LuaCore.LuaLLoadString(LuaState, Chunk);
            if (result != 0)
                return result;

            return LuaCore.LuaPCall(LuaState, 0, -1, 0);
        }

        public static int LuaDoString(KopiLua.LuaState LuaState, string Chunk)
        {
            return LuaCore.LuaLDoString(LuaState, Chunk);
        }

        public static void LuaCreateTable(KopiLua.LuaState LuaState, int NArr, int NRec)
        {
            KopiLua.Lua.LuaCreateTable(LuaState, NArr, NRec);
        }

        public static void LuaNewTable(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaCreateTable(LuaState, 0, 0);
        }

        public static int LuaLDoFile(KopiLua.LuaState LuaState, string FileName)
        {
            int Result = LuaCore.LuaLLoadFile(LuaState, FileName);
            if (Result != 0)
            {
                return Result;
            }

            return LuaCore.LuaPCall(LuaState, 0, -1, 0);
        }

        public static void LuaGetGlobal(KopiLua.LuaState LuaState, string Name)
        {
            LuaCore.LuaPushString(LuaState, Name);
            LuaCore.LuaGetTable(LuaState, LuaIndexes.LUA_GLOBALSINDEX);
        }

        public static void LuaSetGlobal(KopiLua.LuaState LuaState, string Name)
        {
            LuaCore.LuaPushString(LuaState, Name);
            LuaCore.LuaInsert(LuaState, -2);
            LuaCore.LuaSetTable(LuaState, LuaIndexes.LUA_GLOBALSINDEX);
        }

        public static void LuaSetTop(KopiLua.LuaState LuaState, int NewTop)
        {
            KopiLua.Lua.LuaSetTop(LuaState, NewTop);
        }

        public static void LuaPop(KopiLua.LuaState LuaState, int Amount)
        {
            LuaCore.LuaSetTop(LuaState, -(Amount) - 1);
        }

        public static void LuaInsert(KopiLua.LuaState LuaState, int NewTop)
        {
            KopiLua.Lua.LuaInsert(LuaState, NewTop);
        }

        public static void LuaRemove(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaRemove(LuaState, Index);
        }

        public static void LuaGetTable(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaGetTable(LuaState, Index);
        }

        public static void LuaRawGet(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaRawGet(LuaState, Index);
        }

        public static void LuaSetTable(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaSetTable(LuaState, Index);
        }

        public static void LuaRawSet(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaRawSet(LuaState, Index);
        }

        public static void LuaSetMetatable(KopiLua.LuaState LuaState, int ObjIndex)
        {
            KopiLua.Lua.LuaSetMetatable(LuaState, ObjIndex);
        }

        public static int LuaGetMetatable(KopiLua.LuaState LuaState, int ObjIndex)
        {
            return KopiLua.Lua.LuaGetMetatable(LuaState, ObjIndex);
        }

        public static int LuaEqual(KopiLua.LuaState LuaState, int Index1, int Index2)
        {
            return KopiLua.Lua.LuaEqual(LuaState, Index1, Index2);
        }

        public static void LuaPushValue(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaPushValue(LuaState, Index);
        }

        public static void LuaReplace(KopiLua.LuaState LuaState, int Index)
        {
            KopiLua.Lua.LuaReplace(LuaState, Index);
        }

        public static int LuaGetTop(KopiLua.LuaState LuaState)
        {
            return KopiLua.Lua.LuaGetTop(LuaState);
        }

        public static LuaTypes LuaType(KopiLua.LuaState LuaState, int Index)
        {
            return (LuaTypes)KopiLua.Lua.LuaType(LuaState, Index);
        }

        public static bool LuaIsNil(KopiLua.LuaState LuaState, int Index)
        {
            return (LuaCore.LuaType(LuaState, Index) == LuaTypes.LUA_TNIL);
        }

        public static bool LuaIsNumber(KopiLua.LuaState LuaState, int Index)
        {
            return (LuaCore.LuaType(LuaState, Index) == LuaTypes.LUA_TNUMBER);
        }

        public static bool LuaIsBoolean(KopiLua.LuaState LuaState, int Index)
        {
            return LuaCore.LuaType(LuaState, Index) == LuaTypes.LUA_TBOOLEAN;
        }

        public static int LuaLRef(KopiLua.LuaState LuaState, int RegistryIndex)
        {
            return KopiLua.Lua.LuaLRef(LuaState, RegistryIndex);
        }

        public static int LuaRef(KopiLua.LuaState LuaState, int LockRef)
        {
            if (LockRef != 0)
            {
                return LuaCore.LuaLRef(LuaState, LuaIndexes.LUA_REGISTRYINDEX);
            }
            return 0;
        }

        public static void LuaRawGetI(KopiLua.LuaState LuaState, int TableIndex, int Index)
        {
            KopiLua.Lua.LuaRawGetI(LuaState, TableIndex, Index);
        }

        public static void LuaRawSetI(KopiLua.LuaState LuaState, int TableIndex, int Index)
        {
            KopiLua.Lua.LuaRawSetI(LuaState, TableIndex, Index);
        }

        public static object LuaNewUserData(KopiLua.LuaState LuaState, int Size)
        {
            return KopiLua.Lua.LuaNewUserData(LuaState, (uint)Size);
        }

        public static object LuaToUserData(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaToUserData(LuaState, Index);
        }

        public static void LuaGetRef(KopiLua.LuaState LuaState, int Reference)
        {
            LuaCore.LuaRawGetI(LuaState, LuaIndexes.LUA_REGISTRYINDEX, Reference);
        }

        public static void LuaLUnref(KopiLua.LuaState LuaState, int RegistryIndex, int Reference)
        {
            KopiLua.Lua.LuaLUnref(LuaState, RegistryIndex, Reference);
        }

        public static void LuaUnref(KopiLua.LuaState LuaState, int Reference)
        {
            LuaCore.LuaLUnref(LuaState, LuaIndexes.LUA_REGISTRYINDEX, Reference);
        }

        public static bool LuaIsString(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaIsString(LuaState, Index) != 0;
        }

        public static bool LuaIsFunction(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaIsFunction(LuaState, Index);
        }

        public static void LuaPushNil(KopiLua.LuaState LuaState)
        {
            KopiLua.Lua.LuaPushNil(LuaState);
        }

        public static void LuaPushStdCallCFunction(KopiLua.LuaState LuaState, KopiLua.LuaNativeFunction Function)
        {
            KopiLua.Lua.LuaPushCFunction(LuaState, Function);
        }

        public static void LuaCall(KopiLua.LuaState LuaState, int NArgs, int NResults)
        {
            KopiLua.Lua.LuaCall(LuaState, NArgs, NResults);
        }

        public static int LuaPCall(KopiLua.LuaState LuaState, int NArgs, int NResults, int Errfunc)
        {
            return KopiLua.Lua.LuaPCall(LuaState, NArgs, NResults, Errfunc);
        }

        public static KopiLua.LuaNativeFunction LuaToCFunction(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaToCFunction(LuaState, Index);
        }

        public static double LuaToNumber(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaToNumber(LuaState, Index);
        }

        public static bool LuaToBoolean(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaToBoolean(LuaState, Index) != 0;
        }

        public static string LuaToString(KopiLua.LuaState LuaState, int Index)
        {
            // FIXME use the same format string as lua i.e. LUA_NUMBER_FMT
            LuaTypes Typ = LuaType(LuaState, Index);

            if (Typ == LuaTypes.LUA_TNUMBER)
            {
                return string.Format("{0}", LuaToNumber(LuaState, Index));
            }
            else if (Typ == LuaTypes.LUA_TSTRING)
            {
                uint strlen;
                return KopiLua.Lua.LuaToLString(LuaState, Index, out strlen).ToString();
            }
            else if (Typ == LuaTypes.LUA_TNIL)
            {
                return null; // treat lua nulls to as C# nulls
            }
            else
            {
                return "0";	// Because luaV_tostring does this
            }
        }

        public static void LuaAtPanic(KopiLua.LuaState LuaState, KopiLua.LuaNativeFunction Panicf)
        {
            KopiLua.Lua.LuaAtPanic(LuaState, Panicf);
        }

        public static void LuaPushNumber(KopiLua.LuaState LuaState, double Number)
        {
            KopiLua.Lua.LuaPushNumber(LuaState, Number);
        }

        public static void LuaPushBoolean(KopiLua.LuaState LuaState, bool Value)
        {
            KopiLua.Lua.LuaPushBoolean(LuaState, Value ? 1 : 0);
        }

        public static void LuaPushString(KopiLua.LuaState LuaState, string Str)
        {
            KopiLua.Lua.LuaPushString(LuaState, Str);
        }

        public static int LuaLNewMetatable(KopiLua.LuaState LuaState, string Meta)
        {
            return KopiLua.Lua.LuaLNewMetatable(LuaState, Meta);
        }

        public static void LuaGetField(KopiLua.LuaState LuaState, int StackPos, string Meta)
        {
            KopiLua.Lua.LuaGetField(LuaState, StackPos, Meta);
        }

        public static void LuaLGetMetatable(KopiLua.LuaState LuaState, string Meta)
        {
            LuaCore.LuaGetField(LuaState, LuaIndexes.LUA_REGISTRYINDEX, Meta);
        }

        public static Object LuaLCheckUData(KopiLua.LuaState LuaState, int StackPos, string Meta)
        {
            return KopiLua.Lua.LuaLCheckUData(LuaState, StackPos, Meta);
        }

        public static bool LuaLGetMetafield(KopiLua.LuaState LuaState, int StackPos, string Field)
        {
            return KopiLua.Lua.LuaLGetMetafield(LuaState, StackPos, Field) != 0;
        }

        public static int LuaLLoadBuffer(KopiLua.LuaState LuaState, string Buff, int Size, string Name)
        {
            return KopiLua.Lua.LuaLLoadBuffer(LuaState, Buff, (uint)Size, Name);
        }

        public static int LuaLLoadFile(KopiLua.LuaState LuaState, string Filename)
        {
            return KopiLua.Lua.LuaLLoadFile(LuaState, Filename);
        }

        public static bool LuaLCheckMetatable(KopiLua.LuaState LuaState, int Index)
        {
            bool RetVal = false;

            if (LuaGetMetatable(LuaState, Index) != 0)
            {
                LuaPushLightUserData(LuaState, 0);
                LuaRawGet(LuaState, -2);
                RetVal = !LuaIsNil(LuaState, -1);
                LuaSetTop(LuaState, -3);
            }
            return RetVal;
        }


        public static void LuaNetNewUData(KopiLua.LuaState LuaState, int Val)
        {
            byte[] UserData = LuaNewUserData(LuaState, sizeof(int)) as byte[];
            IntToFourBytes(Val, UserData);
        }

        public static int LuaNetToNetObject(KopiLua.LuaState LuaState, int Index)
        {
            byte[] UserData;

            if (LuaType(LuaState, Index) == LuaTypes.LUA_TUSERDATA)
            {
                if (LuaLCheckMetatable(LuaState, Index))
                {
                    UserData = LuaToUserData(LuaState, Index) as byte[];
                    if (UserData != null)
                    {
                        return FourBytesToInt(UserData);
                    }
                }

                UserData = CheckUDataRaw(LuaState, Index, "luaNet_class") as byte[];
                if (UserData != null) return FourBytesToInt(UserData);

                UserData = CheckUDataRaw(LuaState, Index, "luaNet_searchbase") as byte[];
                if (UserData != null) return FourBytesToInt(UserData);

                UserData = CheckUDataRaw(LuaState, Index, "luaNet_function") as byte[];
                if (UserData != null) return FourBytesToInt(UserData);
            }
            return -1;
        }


        public static int LuaNewRawNewObj(KopiLua.LuaState LuaState, int Obj)
        {
            byte[] Bytes = LuaToUserData(LuaState, Obj) as byte[];
            return FourBytesToInt(Bytes);
        }


        public static int LuaNewCheckupData(KopiLua.LuaState LuaState, int UserD, string TName)
        {
            object UserData = CheckUDataRaw(LuaState, UserD, TName);

            if (UserData != null) return FourBytesToInt(UserData as byte[]);
            return -1;
        }


        public static bool LuaCheckStack(KopiLua.LuaState LuaState, int Extra)
        {
            return KopiLua.Lua.LuaCheckStack(LuaState, Extra) != 0;
        }


        public static int LuaNext(KopiLua.LuaState LuaState, int Index)
        {
            return KopiLua.Lua.LuaNext(LuaState, Index);
        }

        public static void LuaPushLightUserData(KopiLua.LuaState LuaState, Object UserData)
        {
            KopiLua.Lua.LuaPushLightUserData(LuaState, UserData);
        }

        public static Object LuaNetGetTag()
        {
            return 0;
        }

        public static void LuaLWhere(KopiLua.LuaState LuaState, int Level)
        {
            KopiLua.Lua.LuaLWhere(LuaState, Level);
        }

        public static KopiLua.LuaState LuaNewThread(KopiLua.LuaState LuaState)
        {
            return KopiLua.Lua.LuaNewThread(LuaState);
        }

        public static LuaThreadStatus LuaResume(KopiLua.LuaState LuaState, int NArgs = 0)
        {
            return (LuaThreadStatus)KopiLua.Lua.LuaResume(LuaState, NArgs);
        }
        #region Utilities
        private static int FourBytesToInt(byte[] Bytes)
        {
            return Bytes[0] + (Bytes[1] << 8) + (Bytes[2] << 16) + (Bytes[3] << 24);
        }

        private static void IntToFourBytes(int Val, byte[] Bytes)
        {
            // gfoot: is this really a good idea?
            Bytes[0] = (byte)Val;
            Bytes[1] = (byte)(Val >> 8);
            Bytes[2] = (byte)(Val >> 16);
            Bytes[3] = (byte)(Val >> 24);
        }
        private static object CheckUDataRaw(KopiLua.LuaState LuaState, int UserD, string TName)
        {
            object Obj = KopiLua.Lua.LuaToUserData(LuaState, UserD);

            if (Obj != null)
            {  /* value is a userdata? */
                if (KopiLua.Lua.LuaGetMetatable(LuaState, UserD) != 0)
                {
                    bool IsEqual;

                    /* does it have a metatable? */
                    KopiLua.Lua.LuaGetField(LuaState, (int)LuaIndexes.LUA_REGISTRYINDEX, TName);  /* get correct metatable */

                    IsEqual = KopiLua.Lua.LuaRawEqual(LuaState, -1, -2) != 0;

                    // NASTY - we need our own version of the lua_pop macro
                    // lua_pop(L, 2);  /* remove both metatables */
                    KopiLua.Lua.LuaSetTop(LuaState, -(2) - 1);

                    //does it have the correct mt?
                    if (IsEqual)  return Obj;
                }
            }

            return null;
        }
        #endregion
    }
}
