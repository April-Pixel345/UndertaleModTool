﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UndertaleModLib.Models
{
    public enum TargetPlatform : int
    {
        os_windows = 0,
        os_uwp = 18,
        os_linux = 6,
        os_macosx = 1,
        os_ios = 3,
        os_android = 4,
        os_ps3 = 16,
        os_ps4 = 14,
        os_psvita = 12,
        os_xbox = 15,
        os_unknown = -1,
        os_3ds = 11,
        os_bb10 = 13,
        os_psp = 2,
        os_symbian = 5,
        os_tizen = 8,
        os_wiiu = 10,
        os_win8native = 9,
        os_xbox360 = 17,
        os_xboxone = 15
    }

    // TODO: INotifyPropertyChanged
    public class UndertaleGeneralInfo : UndertaleObject
    {
        [Flags]
        public enum InfoFlags : uint
        {
            Fullscreen = 0x0001,
            SyncVertex1 = 0x0002,
            SyncVertex2 = 0x0004,
            Interpolate = 0x0008,
            unknown = 0x0010,
            ShowCursor = 0x0020,
            Sizeable = 0x0040,
            ScreenKey = 0x0080,
            SyncVertex3 = 0x0100,
            StudioVersionB1 = 0x0200,
            StudioVersionB2 = 0x0400,
            StudioVersionB3 = 0x0800,
            StudioVersionMask = 0x0E00, // studioVersion = (infoFlags & InfoFlags.StudioVersionMask) >> 9
            SteamEnabled = 0x1000,
            LocalDataEnabled = 0x2000,
            BorderlessWindow = 0x4000,
        }

        public bool DisableDebugger { get; set; } = true;
        public byte BytecodeVersion { get; set; } = 0x10;
        public ushort Unknown { get; set; } = 0;
        public UndertaleString Filename { get; set; }
        public UndertaleString Config { get; set; }
        public uint LastObj { get; set; } = 100000;
        public uint LastTile { get; set; } = 10000000;
        public uint GameID { get; set; } = 13371337;
        public uint Unknown1 { get; set; } = 0;
        public uint Unknown2 { get; set; } = 0;
        public uint Unknown3 { get; set; } = 0;
        public uint Unknown4 { get; set; } = 0;
        public UndertaleString Name { get; set; }
        public uint Major { get; set; } = 1;
        public uint Minor { get; set; } = 0;
        public uint Release { get; set; } = 0;
        public uint Build { get; set; } = 1337;
        public uint DefaultWindowWidth { get; set; } = 1024;
        public uint DefaultWindowHeight { get; set; } = 768;
        public InfoFlags Info { get; set; } = InfoFlags.Interpolate | InfoFlags.unknown | InfoFlags.ShowCursor | InfoFlags.ScreenKey | InfoFlags.StudioVersionB3;
        public byte[] LicenseMD5 { get; set; } = new byte[16];
        public uint LicenseCRC32 { get; set; }
        public uint Timestamp { get; set; } = 0;
        public uint ActiveTargets { get; set; } = 0;
        public UndertaleString DisplayName { get; set; }
        public uint Unknown5 { get; set; } = 0;
        public uint Unknown6 { get; set; } = 0;
        public uint Unknown7 { get; set; } = 0;
        public uint Unknown8 { get; set; } = 0;
        public uint Unknown9 { get; set; } = 0;
        public uint DebuggerPort { get; set; } = 6502;
        public UndertaleSimpleList<RoomOrderEntry> RoomOrder { get; private set; } = new UndertaleSimpleList<RoomOrderEntry>();

        public byte[] GMS2RandomUID { get; set; } = new byte[40]; // License data or encrypted something? Has quite high entropy
        public float GMS2FPS { get; set; } = 30.0f;
        public bool GMS2AllowStatistics { get; set; } = true;
        public byte[] GMS2GameGUID { get; set; } = new byte[16]; // more high entropy data

        public class RoomOrderEntry : UndertaleObject, INotifyPropertyChanged
        {
            private UndertaleResourceById<UndertaleRoom> _Room = new UndertaleResourceById<UndertaleRoom>("ROOM");
            public UndertaleRoom Room { get => _Room.Resource; set { _Room.Resource = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Room")); } }

            public event PropertyChangedEventHandler PropertyChanged;

            public void Serialize(UndertaleWriter writer)
            {
                writer.Write(_Room.Serialize(writer));
            }

            public void Unserialize(UndertaleReader reader)
            {
                _Room.Unserialize(reader, reader.ReadInt32());
            }
        }

        public void Serialize(UndertaleWriter writer)
        {
            writer.Write(DisableDebugger ? (byte)1 : (byte)0);
            writer.Write(BytecodeVersion);
            writer.Write(Unknown);
            writer.WriteUndertaleString(Filename);
            writer.WriteUndertaleString(Config);
            writer.Write(LastObj);
            writer.Write(LastTile);
            writer.Write(GameID);
            writer.Write(Unknown1);
            writer.Write(Unknown2);
            writer.Write(Unknown3);
            writer.Write(Unknown4);
            writer.WriteUndertaleString(Name);
            writer.Write(Major);
            writer.Write(Minor);
            writer.Write(Release);
            writer.Write(Build);
            writer.Write(DefaultWindowWidth);
            writer.Write(DefaultWindowHeight);
            writer.Write((uint)Info);
            if (LicenseMD5.Length != 16)
                throw new IOException("LicenseMD5 has invalid length");
            writer.Write(LicenseMD5);
            writer.Write(LicenseCRC32);
            writer.Write(Timestamp);
            writer.Write(ActiveTargets);
            writer.WriteUndertaleString(DisplayName);
            writer.Write(Unknown5);
            writer.Write(Unknown6);
            writer.Write(Unknown7);
            writer.Write(Unknown8);
            writer.Write(Unknown9);
            writer.Write(DebuggerPort);
            writer.WriteUndertaleObject(RoomOrder);
            if (Major >= 2)
            {
                if (GMS2RandomUID.Length != 40)
                    throw new IOException("GMS2License1 has invalid length");
                writer.Write(GMS2RandomUID);
                writer.Write(GMS2FPS);
                if (GMS2GameGUID.Length != 16)
                    throw new IOException("GMS2License2 has invalid length");
                writer.Write(GMS2AllowStatistics);
                writer.Write(GMS2GameGUID);
            }
        }

        public void Unserialize(UndertaleReader reader)
        {
            DisableDebugger = reader.ReadByte() != 0 ? true : false;
            BytecodeVersion = reader.ReadByte();
            Unknown = reader.ReadUInt16();
            Filename = reader.ReadUndertaleString();
            Config = reader.ReadUndertaleString();
            LastObj = reader.ReadUInt32();
            LastTile = reader.ReadUInt32();
            GameID = reader.ReadUInt32();
            Unknown1 = reader.ReadUInt32();
            Unknown2 = reader.ReadUInt32();
            Unknown3 = reader.ReadUInt32();
            Unknown4 = reader.ReadUInt32();
            Name = reader.ReadUndertaleString();
            Major = reader.ReadUInt32();
            Minor = reader.ReadUInt32();
            Release = reader.ReadUInt32();
            Build = reader.ReadUInt32();
            DefaultWindowWidth = reader.ReadUInt32();
            DefaultWindowHeight = reader.ReadUInt32();
            Info = (InfoFlags)reader.ReadUInt32();
            LicenseMD5 = reader.ReadBytes(16);
            LicenseCRC32 = reader.ReadUInt32();
            Timestamp = reader.ReadUInt32();
            ActiveTargets = reader.ReadUInt32();
            DisplayName = reader.ReadUndertaleString();
            Unknown5 = reader.ReadUInt32();
            Unknown6 = reader.ReadUInt32();
            Unknown7 = reader.ReadUInt32();
            Unknown8 = reader.ReadUInt32();
            Unknown9 = reader.ReadUInt32();
            DebuggerPort = reader.ReadUInt32();
            RoomOrder = reader.ReadUndertaleObject<UndertaleSimpleList<RoomOrderEntry>>();
            if (Major >= 2)
            {
                GMS2RandomUID = reader.ReadBytes(40);
                GMS2FPS = reader.ReadSingle();
                GMS2AllowStatistics = reader.ReadBoolean();
                GMS2GameGUID = reader.ReadBytes(16);
            }
            reader.undertaleData.UnsupportedBytecodeVersion = (BytecodeVersion != 15 && BytecodeVersion != 16);
        }

        public override string ToString()
        {
            return "General info for " + DisplayName + " (GMS version " + Major + "." + Minor + "." + Release + "." + Build + ")";
        }
    }

    public class UndertaleOptions : UndertaleObject
    {
        public uint Unknown1 { get; set; } = 0x80000000;
        public uint Unknown2 { get; set; } = 0x00000002;
        public long Info { get; set; } = 0x00CC7A16; // was supposed to be a copy of UndertaleGeneralInfo.InfoFlags but it doesn't seem like it
        public int Scale { get; set; } = -1;
        public uint WindowColor { get; set; } = 0;
        public uint ColorDepth { get; set; } = 0;
        public uint Resolution { get; set; } = 0;
        public uint Frequency { get; set; } = 0;
        public uint VertexSync { get; set; } = 0;
        public uint Priority { get; set; } = 0;
        public UndertaleSprite.TextureEntry BackImage { get; set; } = null; // Apparently these exist, but I can't find any examples of it
        public UndertaleSprite.TextureEntry FrontImage { get; set; } = null;
        public UndertaleSprite.TextureEntry LoadImage { get; set; } = null;
        public uint LoadAlpha { get; set; } = 255;
        public UndertaleSimpleList<Constant> Constants { get; private set; } = new UndertaleSimpleList<Constant>();

        public class Constant : UndertaleObject
        {
            public UndertaleString Name { get; set; }
            public UndertaleString Value { get; set; }

            public void Serialize(UndertaleWriter writer)
            {
                writer.WriteUndertaleString(Name);
                writer.WriteUndertaleString(Value);
            }

            public void Unserialize(UndertaleReader reader)
            {
                Name = reader.ReadUndertaleString();
                Value = reader.ReadUndertaleString();
            }
        }

        public void Serialize(UndertaleWriter writer)
        {
            writer.Write(Unknown1);
            writer.Write(Unknown2);
            writer.Write(Info);
            writer.Write(Scale);
            writer.Write(WindowColor);
            writer.Write(ColorDepth);
            writer.Write(Resolution);
            writer.Write(Frequency);
            writer.Write(VertexSync);
            writer.Write(Priority);
            writer.WriteUndertaleObject(BackImage);
            writer.WriteUndertaleObject(FrontImage);
            writer.WriteUndertaleObject(LoadImage);
            writer.Write(LoadAlpha);
            writer.WriteUndertaleObject(Constants);
        }

        public void Unserialize(UndertaleReader reader)
        {
            Unknown1 = reader.ReadUInt32();
            Unknown2 = reader.ReadUInt32();
            Info = reader.ReadInt64();
            Scale = reader.ReadInt32();
            WindowColor = reader.ReadUInt32();
            ColorDepth = reader.ReadUInt32();
            Resolution = reader.ReadUInt32();
            Frequency = reader.ReadUInt32();
            VertexSync = reader.ReadUInt32();
            Priority = reader.ReadUInt32();
            BackImage = reader.ReadUndertaleObject<UndertaleSprite.TextureEntry>();
            FrontImage = reader.ReadUndertaleObject<UndertaleSprite.TextureEntry>();
            LoadImage = reader.ReadUndertaleObject<UndertaleSprite.TextureEntry>();
            LoadAlpha = reader.ReadUInt32();
            Constants = reader.ReadUndertaleObject<UndertaleSimpleList<Constant>>();
        }
    }

    public class UndertaleLanguage : UndertaleObject
    {
        // Seems to be a list of entry IDs paired to strings for several languages
        public uint Unknown1 { get; set; } = 1;
        public uint LanguageCount { get; set; } = 0;
        public uint EntryCount { get; set; } = 0;

        public List<UndertaleString> EntryIDs { get; set; } = new List<UndertaleString>();
        public List<LanguageData> Languages { get; set; } = new List<LanguageData>();

        public void Serialize(UndertaleWriter writer)
        {
            writer.Write(Unknown1);
            LanguageCount = (uint)Languages.Count;
            writer.Write(LanguageCount);
            EntryCount = (uint)EntryIDs.Count;
            writer.Write(EntryCount);

            foreach (UndertaleString s in EntryIDs)
            {
                writer.WriteUndertaleString(s);
            }

            foreach (LanguageData ld in Languages)
            {
                ld.Serialize(writer);
            }
        }

        public void Unserialize(UndertaleReader reader)
        {
            Unknown1 = reader.ReadUInt32();
            LanguageCount = reader.ReadUInt32();
            EntryCount = reader.ReadUInt32();

            // Read the identifiers for each entry
            for (int i = 0; i < EntryCount; i++)
            {
                EntryIDs.Add(reader.ReadUndertaleString());
            }

            // Read the data for each language
            for (int i = 0; i < LanguageCount; i++)
            {
                LanguageData ld = new LanguageData();
                ld.Unserialize(reader, EntryCount);
                Languages.Add(ld);
            }
        }

        public class LanguageData
        {
            public UndertaleString Name { get; set; }
            public UndertaleString Region { get; set; }
            public List<UndertaleString> Entries { get; set; } = new List<UndertaleString>();
            // values that correspond to IDs in the main chunk content

            public void Serialize(UndertaleWriter writer)
            {
                writer.WriteUndertaleString(Name);
                writer.WriteUndertaleString(Region);
                foreach (UndertaleString s in Entries)
                {
                    writer.WriteUndertaleString(s);
                }
            }

            public void Unserialize(UndertaleReader reader, uint entryCount)
            {
                Name = reader.ReadUndertaleString();
                Region = reader.ReadUndertaleString();
                for (uint i = 0; i < entryCount; i++)
                {
                    Entries.Add(reader.ReadUndertaleString());
                }
            }
        }
    }
}
