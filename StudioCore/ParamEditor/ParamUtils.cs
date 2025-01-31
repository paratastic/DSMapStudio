using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Numerics;
using ImGuiNET;
using SoulsFormats;

namespace StudioCore.ParamEditor
{
    public class ParamUtils
    {
        public static string Dummy8Write(Byte[] dummy8)
        {
            string val = null;
            foreach (Byte b in dummy8)
            {
                if (val == null)
                    val = "["+b;
                else
                    val += "|"+b;
            }
            if (val == null)
                val = "[]";
            else
                val += "]";
            return val;
        }
        public static Byte[] Dummy8Read(string dummy8, int expectedLength)
        {
            Byte[] nval = new Byte[expectedLength];
            if (!(dummy8.StartsWith('[') && dummy8.EndsWith(']')))
                return null;
            string[] spl = dummy8.Substring(1, dummy8.Length-2).Split('|');
            if (nval.Length != spl.Length)
            {
                return null;
            }
            for (int i=0; i<nval.Length; i++)
            {
                if (!byte.TryParse(spl[i], out nval[i]))
                    return null;
            }
            return nval;
        }
        public static bool RowMatches(PARAM.Row row, PARAM.Row vrow)
        {
            foreach (PARAMDEF.Field field in row.Def.Fields)
            {
                if (field.InternalType == "dummy8" && row[field.InternalName].Value.GetType()==typeof(byte[]))//second check because someone made a dummy8 bit?
                {
                    if (!ByteArrayEquals((byte[])(row[field.InternalName].Value), (byte[])(vrow[field.InternalName].Value)))
                        return false;
                }
                else if (!row[field.InternalName].Value.Equals(vrow[field.InternalName].Value))
                {
                    return false;
                }
            }
            return true;
        }
        public static bool ByteArrayEquals(byte[] v1, byte[] v2) {
            if (v1.Length!=v2.Length)
                return false;
            for (int i=0; i<v1.Length; i++)
            {
                if (v1[i]!=v2[i])
                    return false;
            }
            return true;
        }
    }
}