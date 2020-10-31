﻿using System;
using Alex.API.Utils;
using Alex.Networking.Java.Util;
using fNbt;

namespace Alex.Networking.Java.Packets.Play
{
	public class RespawnPacket : Packet<RespawnPacket>
	{
		public NbtCompound Dimension;
		public byte Difficulty;
		public Gamemode Gamemode, PreviousGamemode;
		public string WorldName;
		public long HashedSeed;
		public bool IsDebug, IsFlat, CopyMetadata;
		
		public override void Decode(MinecraftStream stream)
		{
			Dimension = stream.ReadNbtCompound();
			WorldName = stream.ReadString();
			HashedSeed = stream.ReadLong();
			Gamemode = (Gamemode) stream.ReadByte();
			PreviousGamemode = (Gamemode) stream.ReadByte();
			IsDebug = stream.ReadBool();
			IsFlat = stream.ReadBool();
			CopyMetadata = stream.ReadBool();
		}

		public override void Encode(MinecraftStream stream)
		{
			throw new NotImplementedException();
		}
	}
}
