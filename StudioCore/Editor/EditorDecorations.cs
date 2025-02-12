﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Numerics;
using SoulsFormats;
using ImGuiNET;
using System.Net.Http.Headers;
using System.Security;
using System.Text.RegularExpressions;
using StudioCore;
using StudioCore.Editor;
using StudioCore.ParamEditor;

namespace StudioCore.Editor
{
    public class EditorDecorations
    {
        private static string _refContextCurrentAutoComplete = "";
        
        public static bool HelpIcon(string id, ref string hint, bool canEdit)
        {
            if (hint == null)
                return false;
            return UIHints.AddImGuiHintButton(id, ref hint, canEdit, true); //presently a hack, move code here
        }

        public static void ParamRefText(List<string> paramRefs)
        {
            if (paramRefs == null)
                return;
            if (ParamEditor.ParamEditorScreen.HideReferenceRowsPreference == false) //Move preference
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                ImGui.TextUnformatted($@"  <{String.Join(',', paramRefs)}>");
                ImGui.PopStyleColor();
            }
        }
        public static void ParamRefsSelectables(List<string> paramRefs, dynamic oldval)
        {
            if (paramRefs == null)
                return;
            // Add named row and context menu
            // Lists located params
            // May span lines
            List<(PARAM.Row, string)> matches = resolveRefs(paramRefs, oldval);
            bool entryFound = matches.Count > 0;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
            ImGui.BeginGroup();
            foreach ((PARAM.Row row, string hint) in matches)
            {
                if (row.Name == null || row.Name.Trim().Equals(""))
                    ImGui.TextUnformatted("Unnamed Row");
                else
                    ImGui.TextUnformatted(row.Name + hint);
            }
            ImGui.PopStyleColor();
            if (!entryFound)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                ImGui.TextUnformatted("___");
                ImGui.PopStyleColor();
            }
            ImGui.EndGroup();
        }
        private static List<(PARAM.Row, string)> resolveRefs(List<string> paramRefs, dynamic oldval)
        {
            List<(PARAM.Row, string)> rows = new List<(PARAM.Row, string)>();
            int originalValue = (int)oldval; //make sure to explicitly cast from dynamic or C# complains. Object or Convert.ToInt32 fail.
            foreach (string rt in paramRefs)
            {
                string hint = "";
                if (ParamEditor.ParamBank.Params.ContainsKey(rt))
                {
                    PARAM param = ParamEditor.ParamBank.Params[rt];
                    ParamEditor.ParamMetaData meta = ParamEditor.ParamMetaData.Get(ParamEditor.ParamBank.Params[rt].AppliedParamdef);
                    if (meta != null && meta.Row0Dummy && originalValue == 0)
                        continue;
                    PARAM.Row r = param[originalValue];
                    if (r == null && originalValue > 0 && meta != null)
                    {
                        int altval = originalValue;
                        if (meta.FixedOffset != 0)
                        {
                            altval = originalValue + meta.FixedOffset;
                            hint += meta.FixedOffset > 0 ? "+" + meta.FixedOffset.ToString() : meta.FixedOffset.ToString();
                        }
                        if (meta.OffsetSize > 0)
                        {
                            altval = altval - altval % meta.OffsetSize;
                            hint += "+" + (originalValue % meta.OffsetSize).ToString();
                        }
                        r = ParamEditor.ParamBank.Params[rt][altval];
                    }
                    if (r == null)
                        continue;
                    rows.Add((r, hint));
                }
            }
            return rows;
        }

        public static void EnumNameText(string enumName)
        {
            if (enumName != null && ParamEditor.ParamEditorScreen.HideEnumsPreference == false) //Move preference
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                ImGui.TextUnformatted($@"  {enumName}");
                ImGui.PopStyleColor();
            }
        }
        public static void EnumValueText(Dictionary<string, string> enumValues, string value)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.5f, 0.5f, 1.0f));
            ImGui.TextUnformatted(enumValues.GetValueOrDefault(value, "Not Enumerated"));
            ImGui.PopStyleColor();
        }

        public static void VirtualParamRefSelectables(string virtualRefName, object searchValue)
        {
            // Add Goto statements
            foreach (var param in ParamEditor.ParamBank.Params)
            {
                PARAMDEF.Field foundfield = null;
                //get field
                foreach (PARAMDEF.Field f in param.Value.AppliedParamdef.Fields)
                {
                    if (ParamEditor.FieldMetaData.Get(f).VirtualRef != null && ParamEditor.FieldMetaData.Get(f).VirtualRef.Equals(virtualRefName))
                    {
                        foundfield = f;
                        break;
                    }
                }

                if (foundfield == null)
                    continue;
                //add selectable
                if (ImGui.Selectable($@"Go to first in {param.Key}"))
                {
                    foreach (PARAM.Row row in param.Value.Rows)
                    {
                        if (row[foundfield.InternalName].Value.ToString().Equals(searchValue.ToString()))
                        {
                            EditorCommandQueue.AddCommand($@"param/select/-1/{param.Key}/{row.ID}");
                            break;
                        }
                    }
                }
            }
        }
        
        public static bool ParamRefEnumContextMenu(object oldval, ref object newval, List<string> RefTypes, ParamEnum Enum)
        {
            if (RefTypes == null && Enum == null)
                return false;
            bool result = false;
            if (ImGui.BeginPopupContextItem("rowMetaValue"))
            {
                if (RefTypes != null)
                    result |= PropertyRowRefsContextItems(RefTypes, oldval, ref newval);
                if (Enum != null)
                    result |= PropertyRowEnumContextItems(Enum, oldval, ref newval);
                ImGui.EndPopup();
            }
            return result;
        }

        public static bool PropertyRowRefsContextItems(List<string> reftypes, dynamic oldval, ref object newval)
        {
            // Add Goto statements
            foreach (string rt in reftypes)
            {
                if (!ParamBank.Params.ContainsKey(rt))
                    continue;
                int searchVal = (int)oldval;
                ParamMetaData meta = ParamMetaData.Get(ParamBank.Params[rt].AppliedParamdef);
                if (meta != null)
                {
                    if (meta.Row0Dummy && searchVal == 0)
                        continue;
                    if (meta.FixedOffset != 0 && searchVal > 0)
                    {
                        searchVal = searchVal + meta.FixedOffset;
                    }
                    if (meta.OffsetSize > 0 && searchVal > 0 && ParamBank.Params[rt][(int)searchVal] == null)
                    {
                        searchVal = (int)searchVal - (int)oldval % meta.OffsetSize;
                    }
                }
                if (ParamBank.Params[rt][searchVal] != null)
                {
                    if (ImGui.Selectable($@"Go to {rt}"))
                        EditorCommandQueue.AddCommand($@"param/select/-1/{rt}/{searchVal}");
                    if (ImGui.Selectable($@"Go to {rt} in new view"))
                        EditorCommandQueue.AddCommand($@"param/select/new/{rt}/{searchVal}");
                }
            }
            // Add searchbar for named editing
            ImGui.InputText("##value", ref _refContextCurrentAutoComplete, 128);
            // This should be replaced by a proper search box with a scroll and everything
            if (_refContextCurrentAutoComplete != "")
            {
                foreach (string rt in reftypes)
                {
                    if (!ParamBank.Params.ContainsKey(rt))
                        continue;
                    ParamMetaData meta = ParamMetaData.Get(ParamBank.Params[rt].AppliedParamdef);
                    int maxResultsPerRefType = 15 / reftypes.Count;
                    List<PARAM.Row> rows = RowSearchEngine.rse.Search(ParamBank.Params[rt], _refContextCurrentAutoComplete, true, true);
                    foreach (PARAM.Row r in rows)
                    {
                        if (maxResultsPerRefType <= 0)
                            break;
                        if (ImGui.Selectable(r.ID + ": " + r.Name))
                        {
                            if (meta != null && meta.FixedOffset != 0)
                                newval = (int)r.ID - meta.FixedOffset;
                            else
                                newval = (int)r.ID;
                            _refContextCurrentAutoComplete = "";
                            return true;
                        }
                        maxResultsPerRefType--;
                    }
                }
            }
            return false;
        }
        public static bool PropertyRowEnumContextItems(ParamEnum en, object oldval, ref object newval)
        {
            try
            {
                foreach (KeyValuePair<string, string> option in en.values)
                {
                    if (ImGui.Selectable($"{option.Key}: {option.Value}"))
                    {
                        newval = Convert.ChangeType(option.Key, oldval.GetType());
                        return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }
    }
}