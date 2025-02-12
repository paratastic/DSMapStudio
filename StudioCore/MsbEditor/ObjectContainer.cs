﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Xml.Serialization;
using SoulsFormats;
using StudioCore.Scene;

namespace StudioCore.MsbEditor
{
    /// <summary>
    /// High level class that stores a single map (msb) and can serialize/
    /// deserialize it. This is the logical portion of the map and does not
    /// handle tasks like rendering or loading associated assets with it.
    /// </summary>
    public class ObjectContainer
    {
        public string Name { get; set; }

        [XmlIgnore]
        public List<Entity> Objects = new List<Entity>();
        public Entity RootObject { get; set; }
        [XmlIgnore]
        public Universe Universe { get; protected set; }

        public bool HasUnsavedChanges { get; set; } = false;

        public ObjectContainer()
        {

        }

        public ObjectContainer(Universe u, string name)
        {
            Name = name;
            Universe = u;
            var t = new TransformNode();
            RootObject = new Entity(this, t);
        }

        public void AddObject(Entity obj)
        {
            Objects.Add(obj);
            RootObject.AddChild(obj);
        }

        public void Clear()
        {
            Objects.Clear();
        }

        public Entity GetObjectByName(string name)
        {
            foreach (var m in Objects)
            {
                if (m.Name == name)
                {
                    return m;
                }
            }
            return null;
        }

        public byte GetNextUnique(string prop, byte value)
        {
            HashSet<byte> usedvals = new HashSet<byte>();
            foreach (var obj in Objects)
            {
                if (obj.GetPropertyValue(prop) != null)
                {
                    byte val = obj.GetPropertyValue<byte>(prop);
                    usedvals.Add(val);
                }
            }

            for (int i = 0; i < 256; i++)
            {
                if (!usedvals.Contains((byte)((value + i) % 256)))
                {
                    return (byte)((value + i) % 256);
                }
            }
            return value;
        }

        public void LoadFlver(FLVER2 flver, MeshRenderableProxy proxy)
        {
            var meshesNode = new NamedEntity(this, null, "Meshes");
            Objects.Add(meshesNode);
            RootObject.AddChild(meshesNode);
            for (int i = 0; i < flver.Meshes.Count; i++)
            {
                var meshnode = new NamedEntity(this, flver.Meshes[i], $@"mesh_{i}");
                if (proxy.Submeshes.Count > 0)
                {
                    meshnode.RenderSceneMesh = proxy.Submeshes[i];
                    proxy.Submeshes[i].SetSelectable(meshnode);
                }
                Objects.Add(meshnode);
                meshesNode.AddChild(meshnode);
            }

            var materialsNode = new NamedEntity(this, null, "Materials");
            Objects.Add(materialsNode);
            RootObject.AddChild(materialsNode);
            for (int i = 0; i < flver.Materials.Count; i++)
            {
                var matnode = new Entity(this, flver.Materials[i]);
                Objects.Add(matnode);
                materialsNode.AddChild(matnode);
            }

            var layoutsNode = new NamedEntity(this, null, "Layouts");
            Objects.Add(layoutsNode);
            RootObject.AddChild(layoutsNode);
            for (int i = 0; i < flver.BufferLayouts.Count; i++)
            {
                var laynode = new NamedEntity(this, flver.BufferLayouts[i], $@"layout_{i}");
                Objects.Add(laynode);
                layoutsNode.AddChild(laynode);
            }

            var bonesNode = new NamedEntity(this, null, "Bones");
            Objects.Add(bonesNode);
            RootObject.AddChild(bonesNode);
            var boneEntList = new List<TransformableNamedEntity>();
            for (int i = 0; i < flver.Bones.Count; i++)
            {
                var bonenode = new TransformableNamedEntity(this, flver.Bones[i], flver.Bones[i].Name);
                bonenode.RenderSceneMesh = Universe.GetBoneDrawable(this, bonenode);
                Objects.Add(bonenode);
                boneEntList.Add(bonenode);
            }
            for (int i = 0; i < flver.Bones.Count; i++)
            {
                if (flver.Bones[i].ParentIndex == -1)
                {
                    bonesNode.AddChild(boneEntList[i]);
                }
                else
                {
                    boneEntList[flver.Bones[i].ParentIndex].AddChild(boneEntList[i]);
                }
            }

            // Add dummy polys attached to bones
            var dmysNode = new NamedEntity(this, null, "DummyPolys");
            Objects.Add(dmysNode);
            RootObject.AddChild(dmysNode);
            for (int i = 0; i < flver.Dummies.Count; i++)
            {
                var dmynode = new TransformableNamedEntity(this, flver.Dummies[i], $@"dmy_{i}");
                dmynode.RenderSceneMesh = Universe.GetDummyPolyDrawable(this, dmynode);
                Objects.Add(dmynode);
                dmysNode.AddChild(dmynode);
            }
        }
    }

    public class Map : ObjectContainer
    {
        public List<GPARAM> GParams { get; private set; }

        /// <summary>
        /// The map offset used to transform light and ds2 generators
        /// </summary>
        public Transform MapOffset { get; set; } = Transform.Default;

        // This keeps all models that exist when loading a map, so that saves
        // can be byte perfect
        private Dictionary<string, IMsbModel> LoadedModels = new Dictionary<string, IMsbModel>();

        public Map(Universe u, string mapid)
        {
            Name = mapid;
            Universe = u;
            var t = new TransformNode(mapid);
            RootObject = new MapEntity(this, t, MapEntity.MapEntityType.MapRoot);
        }

        public void LoadMSB(IMsb msb)
        {
            foreach (var m in msb.Models.GetEntries())
            {
                LoadedModels.Add(m.Name, m);
            }

            foreach (var p in msb.Parts.GetEntries())
            {
                var n = new MapEntity(this, p, MapEntity.MapEntityType.Part);
                Objects.Add(n);
                RootObject.AddChild(n);
            }

            foreach (var p in msb.Regions.GetEntries())
            {
                var n = new MapEntity(this, p, MapEntity.MapEntityType.Region);
                Objects.Add(n);
                RootObject.AddChild(n);
            }

            foreach (var p in msb.Events.GetEntries())
            {
                var n = new MapEntity(this, p, MapEntity.MapEntityType.Event);
                Objects.Add(n);
                RootObject.AddChild(n);
            }

            foreach (var m in Objects)
            {
                m.BuildReferenceMap();
            }
        }

        private void AddModelDeS(IMsb m, MSBD.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            if (model is MSBD.Model.MapPiece)
            {
                model.SibPath = $@"N:\DemonsSoul\data\Model\map\{Name}\sib\{name}.sib";
            }
            else if (model is MSBD.Model.Object)
            {
                model.SibPath = $@"N:\DemonsSoul\data\Model\obj\{name}\sib\{name}.sib";
            }
            else if (model is MSBD.Model.Enemy)
            {
                model.SibPath = $@"N:\DemonsSoul\data\Model\chr\{name}\sib\{name}.sib";
            }
            else if (model is MSBD.Model.Collision)
            {
                model.SibPath = $@"N:\DemonsSoul\data\Model\map\{Name}\hkxwin\{name}.hkxwin";
            }
            else if (model is MSBD.Model.Navmesh)
            {
                model.SibPath = $@"N:\DemonsSoul\data\Model\map\{Name}\navimesh\{name}.SIB";
            }
            m.Models.Add(model);
        }

        private void AddModelDS1(IMsb m, MSB1.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            if (model is MSB1.Model.MapPiece)
            {
                model.SibPath = $@"N:\FRPG\data\Model\map\{Name}\sib\{name}.sib";
            }
            else if (model is MSB1.Model.Object)
            {
                model.SibPath = $@"N:\FRPG\data\Model\obj\{name}\sib\{name}.sib";
            }
            else if (model is MSB1.Model.Enemy)
            {
                model.SibPath = $@"N:\FRPG\data\Model\chr\{name}\sib\{name}.sib";
            }
            else if (model is MSB1.Model.Collision)
            {
                model.SibPath = $@"N:\FRPG\data\Model\map\{Name}\hkxwin\{name}.hkxwin";
            }
            else if (model is MSB1.Model.Navmesh)
            {
                model.SibPath = $@"N:\FRPG\data\Model\map\{Name}\navimesh\{name}.sib";
            }
            m.Models.Add(model);
        }

        private void AddModelDS2(IMsb m, MSB2.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            m.Models.Add(model);
        }

        private void AddModelBB(IMsb m, MSBB.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            var a = $@"A{Name.Substring(1, 2)}";
            model.Name = name;
            if (model is MSBB.Model.MapPiece)
            {
                model.SibPath = $@"N:\SPRJ\data\Model\map\{Name}\sib\{name}{a}.sib";
            }
            else if (model is MSBB.Model.Object)
            {
                model.SibPath = $@"N:\SPRJ\data\Model\obj\{name.Substring(0, 3)}\{name}\sib\{name}.sib";
            }
            else if (model is MSBB.Model.Enemy)
            {
                // Not techincally required but doing so means that unedited bloodborne maps
                // will write identical to the original byte for byte
                if (name == "c0000")
                {
                    model.SibPath = $@"N:\SPRJ\data\Model\chr\{name}\sib\{name}.SIB";
                }
                else
                {
                    model.SibPath = $@"N:\SPRJ\data\Model\chr\{name}\sib\{name}.sib";
                }
            }
            else if (model is MSBB.Model.Collision)
            {
                model.SibPath = $@"N:\SPRJ\data\Model\map\{Name}\hkt\{name}{a}.hkt";
            }
            else if (model is MSBB.Model.Navmesh)
            {
                model.SibPath = $@"N:\SPRJ\data\Model\map\{Name}\navimesh\{name}{a}.sib";
            }
            else if (model is MSBB.Model.Other)
            {
                model.SibPath = $@"";
            }
            m.Models.Add(model);
        }

        private void AddModelDS3(IMsb m, MSB3.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            if (model is MSB3.Model.MapPiece)
            {
                model.SibPath = $@"N:\FDP\data\Model\map\{Name}\sib\{name}.sib";
            }
            else if (model is MSB3.Model.Object)
            {
                model.SibPath = $@"N:\FDP\data\Model\obj\{name}\sib\{name}.sib";
            }
            else if (model is MSB3.Model.Enemy)
            {
                model.SibPath = $@"N:\FDP\data\Model\chr\{name}\sib\{name}.sib";
            }
            else if (model is MSB3.Model.Collision)
            {
                model.SibPath = $@"N:\FDP\data\Model\map\{Name}\hkt\{name}.hkt";
            }
            else if (model is MSB3.Model.Other)
            {
                model.SibPath = $@"";
            }
            m.Models.Add(model);
        }

        private void AddModelSekiro(IMsb m, MSBS.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            if (model is MSBS.Model.MapPiece)
            {
                model.SibPath = $@"N:\FDP\data\Model\map\{Name}\sib\{name}.sib";
            }
            else if (model is MSBS.Model.Object)
            {
                model.SibPath = $@"N:\FDP\data\Model\obj\{name}\sib\{name}.sib";
            }
            else if (model is MSBS.Model.Enemy)
            {
                model.SibPath = $@"N:\FDP\data\Model\chr\{name}\sib\{name}.sib";
            }
            else if (model is MSBS.Model.Collision)
            {
                model.SibPath = $@"N:\FDP\data\Model\map\{Name}\hkt\{name}.hkt";
            }
            else if (model is MSBS.Model.Player)
            {
                model.SibPath = $@"";
            }
            m.Models.Add(model);
        }

        private void AddModelER(IMsb m, MSBE.Model model, string name)
        {
            if (LoadedModels[name] != null)
            {
                m.Models.Add(LoadedModels[name]);
                return;
            }
            model.Name = name;
            if (model is MSBE.Model.MapPiece)
            {
                model.SibPath = $@"N:\GR\data\Model\map\{Name}\sib\{name}.sib";
            }
            else if (model is MSBE.Model.Asset)
            {
                model.SibPath = $@"N:\GR\data\Asset\Environment\geometry\{name.Substring(0, 6)}\{name}\sib\{name}.sib";
            }
            else if (model is MSBE.Model.Enemy)
            {
                model.SibPath = $@"N:\GR\data\Model\chr\{name}\sib\{name}.sib";
            }
            else if (model is MSBE.Model.Collision)
            {
                model.SibPath = $@"N:\GR\data\Model\map\{Name}\hkt\{name}.hkt";
            }
            else if (model is MSBE.Model.Player)
            {
                model.SibPath = $@"N:\GR\data\Model\chr\{name}\sib\{name}.sib";
            }
            m.Models.Add(model);
        }

        private void AddModel<T>(IMsb m, string name) where T : IMsbModel, new()
        {
            var model = new T();
            model.Name = name;
            m.Models.Add(model);
        }

        private void AddModelsDeS(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelDeS(msb, new MSBD.Model.MapPiece(), m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelDeS(msb, new MSBD.Model.Collision(), m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelDeS(msb, new MSBD.Model.Object(), m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelDeS(msb, new MSBD.Model.Enemy(), m);
                }
                if (m.StartsWith("n"))
                {
                    AddModelDeS(msb, new MSBD.Model.Navmesh(), m);
                }
            }
        }

        private void AddModelsDS1(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelDS1(msb, new MSB1.Model.MapPiece(), m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelDS1(msb, new MSB1.Model.Collision(), m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelDS1(msb, new MSB1.Model.Object(), m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelDS1(msb, new MSB1.Model.Enemy(), m);
                }
                if (m.StartsWith("n"))
                {
                    AddModelDS1(msb, new MSB1.Model.Navmesh(), m);
                }
            }
        }

        private void AddModelsDS2(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelDS2(msb, new MSB2.Model.MapPiece(), m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelDS2(msb, new MSB2.Model.Collision(), m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelDS2(msb, new MSB2.Model.Object(), m);
                }
                if (m.StartsWith("n"))
                {
                    AddModelDS2(msb, new MSB2.Model.Navmesh(), m);
                }
            }
        }

        private void AddModelsBB(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelBB(msb, new MSBB.Model.MapPiece() { Name = m }, m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelBB(msb, new MSBB.Model.Collision() { Name = m }, m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelBB(msb, new MSBB.Model.Object() { Name = m }, m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelBB(msb, new MSBB.Model.Enemy() { Name = m }, m);
                }
                if (m.StartsWith("n"))
                {
                    AddModelBB(msb, new MSBB.Model.Navmesh() { Name = m }, m);
                }
            }
        }

        private void AddModelsDS3(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelDS3(msb, new MSB3.Model.MapPiece() { Name = m }, m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelDS3(msb, new MSB3.Model.Collision() { Name = m }, m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelDS3(msb, new MSB3.Model.Object() { Name = m }, m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelDS3(msb, new MSB3.Model.Enemy() { Name = m }, m);
                }
            }
        }

        private void AddModelsSekiro(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelSekiro(msb, new MSBS.Model.MapPiece() { Name = m }, m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelSekiro(msb, new MSBS.Model.Collision() { Name = m }, m);
                }
                if (m.StartsWith("o"))
                {
                    AddModelSekiro(msb, new MSBS.Model.Object() { Name = m }, m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelSekiro(msb, new MSBS.Model.Enemy() { Name = m }, m);
                }
            }
        }

        private void AddModelsER(IMsb msb)
        {
            foreach (var mk in LoadedModels.OrderBy(q => q.Key))
            {
                var m = mk.Key;
                if (m.StartsWith("m"))
                {
                    AddModelER(msb, new MSBE.Model.MapPiece() { Name = m }, m);
                }
                if (m.StartsWith("h"))
                {
                    AddModelER(msb, new MSBE.Model.Collision() { Name = m }, m);
                }
                if (m.StartsWith("AEG"))
                {
                    AddModelER(msb, new MSBE.Model.Asset() { Name = m }, m);
                }
                if (m.StartsWith("c"))
                {
                    AddModelER(msb, new MSBE.Model.Enemy() { Name = m }, m);
                }
            }
        }

        public void SerializeToMSB(IMsb msb, GameType game)
        {
            foreach (var m in Objects)
            {
                if (m.WrappedObject != null && m.WrappedObject is IMsbPart p)
                {
                    msb.Parts.Add(p);
                    if (p.ModelName != null && !LoadedModels.ContainsKey(p.ModelName))
                    {
                        LoadedModels.Add(p.ModelName, null);
                    }
                }
                else if (m.WrappedObject != null && m.WrappedObject is IMsbRegion r)
                {
                    msb.Regions.Add(r);
                }
                else if (m.WrappedObject != null && m.WrappedObject is IMsbEvent e)
                {
                    msb.Events.Add(e);
                }
            }

            if (game == GameType.DemonsSouls)
            {
                AddModelsDeS(msb);
            }
            else if (game == GameType.DarkSoulsPTDE || game == GameType.DarkSoulsRemastered)
            {
                AddModelsDS1(msb);
            }
            else if (game == GameType.DarkSoulsIISOTFS)
            {
                AddModelsDS2(msb);
            }
            else if (game == GameType.Bloodborne)
            {
                AddModelsBB(msb);
            }
            else if (game == GameType.DarkSoulsIII)
            {
                AddModelsDS3(msb);
            }
            else if (game == GameType.Sekiro)
            {
                AddModelsSekiro(msb);
            }
            else if (game == GameType.EldenRing)
            {
                AddModelsER(msb);
            }
        }

        public void SerializeToXML(XmlSerializer serializer, TextWriter writer, GameType game)
        {
            serializer.Serialize(writer, this);
        }

        public bool SerializeDS2Generators(PARAM locations, PARAM generators)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (var o in Objects)
            {
                if (o is MapEntity m && m.Type == MapEntity.MapEntityType.DS2Generator && m.WrappedObject is MergedParamRow mp)
                {
                    if (!ids.Contains(mp.ID))
                    {
                        ids.Add(mp.ID);
                    }
                    else
                    {
                        MessageBox.Show($@"{mp.Name} has an ID that's already used. Please change it to something unique and save again.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    var loc = mp.GetRow("generator-loc");
                    if (loc != null)
                    {
                        // Adjust the location to be relative to the mapoffset
                        var newloc = new PARAM.Row(loc);
                        newloc["PositionX"].Value = (float)loc["PositionX"].Value - MapOffset.Position.X;
                        newloc["PositionY"].Value = (float)loc["PositionY"].Value - MapOffset.Position.Y;
                        newloc["PositionZ"].Value = (float)loc["PositionZ"].Value - MapOffset.Position.Z;
                        locations.Rows.Add(newloc);
                    }
                    var gen = mp.GetRow("generator");
                    if (gen != null)
                    {
                        generators.Rows.Add(gen);
                    }
                }
            }
            return true;
        }

        public bool SerializeDS2Regist(PARAM regist)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (var o in Objects)
            {
                if (o is MapEntity m && m.Type == MapEntity.MapEntityType.DS2GeneratorRegist && m.WrappedObject is PARAM.Row mp)
                {
                    if (!ids.Contains(mp.ID))
                    {
                        ids.Add(mp.ID);
                    }
                    else
                    {
                        MessageBox.Show($@"{mp.Name} has an ID that's already used. Please change it to something unique and save again.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    regist.Rows.Add(mp);
                }
            }
            return true;
        }

        public bool SerializeDS2Events(PARAM evs)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (var o in Objects)
            {
                if (o is MapEntity m && m.Type == MapEntity.MapEntityType.DS2Event && m.WrappedObject is PARAM.Row mp)
                {
                    if (!ids.Contains(mp.ID))
                    {
                        ids.Add(mp.ID);
                    }
                    else
                    {
                        MessageBox.Show($@"{mp.Name} has an ID that's already used. Please change it to something unique and save again.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    var newloc = new PARAM.Row(mp);
                    evs.Rows.Add(newloc);
                }
            }
            return true;
        }

        public bool SerializeDS2EventLocations(PARAM locs)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (var o in Objects)
            {
                if (o is MapEntity m && m.Type == MapEntity.MapEntityType.DS2EventLocation && m.WrappedObject is PARAM.Row mp)
                {
                    if (!ids.Contains(mp.ID))
                    {
                        ids.Add(mp.ID);
                    }
                    else
                    {
                        MessageBox.Show($@"{mp.Name} has an ID that's already used. Please change it to something unique and save again.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    // Adjust the location to be relative to the mapoffset
                    var newloc = new PARAM.Row(mp);
                    newloc["PositionX"].Value = (float)mp["PositionX"].Value - MapOffset.Position.X;
                    newloc["PositionY"].Value = (float)mp["PositionY"].Value - MapOffset.Position.Y;
                    newloc["PositionZ"].Value = (float)mp["PositionZ"].Value - MapOffset.Position.Z;
                    locs.Rows.Add(newloc);
                }
            }
            return true;
        }

        public bool SerializeDS2ObjInstances(PARAM objs)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (var o in Objects)
            {
                if (o is MapEntity m && m.Type == MapEntity.MapEntityType.DS2ObjectInstance && m.WrappedObject is PARAM.Row mp)
                {
                    if (!ids.Contains(mp.ID))
                    {
                        ids.Add(mp.ID);
                    }
                    else
                    {
                        MessageBox.Show($@"{mp.Name} has an ID that's already used. Please change it to something unique and save again.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    var newobj = new PARAM.Row(mp);
                    objs.Rows.Add(newobj);
                }
            }
            return true;
        }

        public MapSerializationEntity SerializeHierarchy()
        {
            Dictionary<Entity, int> idmap = new Dictionary<Entity, int>();
            for (int i = 0; i < Objects.Count; i++)
            {
                idmap.Add(Objects[i], i);
            }
            return ((MapEntity)RootObject).Serialize(idmap);
        }
    }
}
