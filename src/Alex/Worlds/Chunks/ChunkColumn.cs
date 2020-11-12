﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Alex.API;
using Alex.API.Blocks;
using Alex.API.Utils;
using Alex.API.World;
using Alex.Blocks;
using Alex.Blocks.State;
using Alex.Entities.BlockEntities;
using Alex.Graphics.Models.Blocks;
using Alex.Networking.Java.Util;
using Alex.Worlds.Abstraction;
using Alex.Worlds.Singleplayer;
using fNbt;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NLog;

namespace Alex.Worlds.Chunks
{
	public class ChunkColumn
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(SPWorldProvider));

		public const int ChunkHeight = 256;
		public const int ChunkWidth = 16;
		public const int ChunkDepth = 16;

		public int X { get; set; }
		public int Z { get; set; }

		public          bool IsNew           { get; set; } = true;
		public          bool SkyLightDirty   =>  Sections != null && Sections.Sum(x => x.SkyLightUpdates) > 0; 
		public          bool BlockLightDirty => Sections != null && Sections.Sum(x => x.BlockLightUpdates) > 0; 
		public readonly Stopwatch LightUpdateWatch = new Stopwatch();
		public          ChunkSection[] Sections { get; set; } = new ChunkSection[16];
		public          int[] BiomeId = ArrayOf<int>.Create(16 * 16 * 256, 1);
		public          short[] Height = new short[256];
		
		public  object                                              UpdateLock { get; set; } = new object();
		private ConcurrentDictionary<BlockCoordinates, BlockEntity> BlockEntities { get; }
		public  BlockEntity[]                                       GetBlockEntities => BlockEntities.Values.ToArray();
		
		internal ChunkData ChunkData { get; private set; }
		private object _dataLock = new object();
		public ChunkColumn()
		{
			for (int i = 0; i < Sections.Length; i++)
			{
				Sections[i] = null;
			}
			
			BlockEntities = new ConcurrentDictionary<BlockCoordinates, BlockEntity>();
			LightUpdateWatch.Start();
			
			ChunkData = new ChunkData();
		}

		public void ScheduleBorder()
		{
			for (int sectionIndex = 0; sectionIndex < 16; sectionIndex++)
			{
				var section = Sections[sectionIndex];

				if (section == null)
					continue;

				for (int x = 0; x < 16; x++)
				{
					for (int z = 0; z < 16; z++)
					{
						for (int y = 0; y < 16; y++)
						{
							if (x == 0 || x == 15 || z == 0 || z == 15)
							{
								section.SetScheduled(x,y,z, true);
							}
						}
					}
				}
			}
		}

		public void UpdateBuffer(GraphicsDevice device, IBlockAccess world)
		{
			lock (_dataLock)
			{
				var chunkPosition = new Vector3(X << 4, 0, Z << 4);
				for (int sectionIndex = 0; sectionIndex < 16; sectionIndex++)
				{
					var section = Sections[sectionIndex];

					if (section == null)
						continue;

					var sectionY = (sectionIndex << 4);
					
					for (int x = 0; x < 16; x++)
					{
						for (int z = 0; z < 16; z++)
						{
							for (int y = 0; y < 16; y++)
							{
								if (!IsNew && !section.IsScheduled(x, y, z) &&
								    !section.IsBlockLightScheduled(x, y, z) &&
								    !section.IsSkylightUpdateScheduled(x, y, z))
									continue;

								try
								{
									var by               = sectionY + y;
									var blockCoordinates = new BlockCoordinates(x, by, z);

									ChunkData.Remove(device, blockCoordinates);

									//var position = chunkPosition + new Vector3(x, by, z);
									
									var blockPosition = new BlockCoordinates(
										(int) (chunkPosition.X + x), y + (sectionIndex << 4), (int) (chunkPosition.Z + z));
									
									foreach (var state in section.GetAll(x, y, z))
									{
										var blockState = state.State;
										if (blockState == null || blockState.Model == null || blockState.Block == null || !blockState.Block.Renderable)
											continue;
										
										var model = blockState.Model;

										if (blockState != null && blockState.Block.RequiresUpdate)
										{
											var newblockState = blockState.Block.BlockPlaced(
												world, blockState, blockPosition);

											if (newblockState != blockState)
											{
												blockState = newblockState;

												section.Set(state.Storage, x, y, z, blockState);
												model = blockState.Model;
											}
										}

										if (blockState.IsMultiPart)
										{
											var newBlockState = MultiPartModelHelper.GetBlockState(
												world, blockPosition, blockState, blockState.MultiPartHelper);

											if (newBlockState != blockState)
											{
												blockState = newBlockState;

												section.Set(state.Storage, x, y, z, blockState);
												model = blockState.Model;
											}
										}
										
										model.GetVertices(world, ChunkData, blockPosition, blockState.Block);
									}
								}
								finally
								{
									section.SetScheduled(x, y, z, false);
									section.SetBlockLightScheduled(x, y, z, false);
									section.SetSkyLightUpdateScheduled(x, y, z, false);
								}
							}
						}
					}
				}
				
				ChunkData.ApplyChanges(device);
				IsNew = false;
			}
		}
		
		public IEnumerable<BlockCoordinates> GetLightSources()
		{
			for (int i = 0; i < Sections.Length; i++)
			{
				var section = Sections[i];
				if (section == null)
					continue;
				
				foreach (var ls in section.LightSources.ToArray())
				{
					yield return new BlockCoordinates(ls.X, (i * 16) + ls.Y, ls.Z);
				}
			}
		}
		
		protected virtual ChunkSection CreateSection(bool storeSkylight, int sections)
		{
			return new ChunkSection(this, storeSkylight, sections);
		}

		public ChunkSection GetSection(int y)
		{
			var section = Sections[y >> 4];
			if (section == null)
			{
				var storage = CreateSection(true, 2);
				Sections[y >> 4] = storage;
				return storage;
			}

			return (ChunkSection) section;
		}

		public void SetBlockState(int x, int y, int z, BlockState blockState)
		{
			SetBlockState(x, y, z, blockState, 0);
		}

		public void SetBlockState(int x, int y, int z, BlockState state, int storage)
		{
			if ((x < 0 || x > ChunkWidth) || (y < 0 || y > ChunkHeight) || (z < 0 || z > ChunkDepth))
				return;

			GetSection(y).Set(storage, x, y - 16 * (y >> 4), z, state);

			_heightDirty = true;
		}

		public void RecalculateHeight(int x, int z, bool doLighting = true)
		{
			bool inLight = doLighting;
			
			for (int y = 255; y > 0; y--)
			{
				if (inLight)
				{
					var section = GetSection(y);
					var block = section.Get(x, y - ((@y >> 4) << 4), z).Block;

					if (!block.Renderable || (!block.BlockMaterial.BlocksLight))
					{
						SetSkyLight(x, y, z, 15);
					}
					else
					{
						SetHeight(x, z, (short) (y + 1));
						SetSkyLight(x, y, z, 0);
						inLight = false;
					}
				}
				else
				{
					SetSkyLight(x, y, z, (byte) (doLighting ? 0 : 15));
				}
			}
		}

		public int GetRecalculatedHeight(int x, int z)
		{
			bool isInAir = true;

			for (int y = 255; y >= 0; y--)
			{
				{
					var chunk = GetSection(y);
					if (isInAir && chunk.IsAllAir)
					{
						if (chunk.IsDirty) Array.Fill<byte>(chunk.SkyLight.Data, 0xff);
						y -= 15;
						continue;
					}

					isInAir = false;

					var block = GetBlockState(x, y, z).Block;

					if (!block.Renderable || (block.Transparent && !block.BlockMaterial.BlocksLight))
						continue;

					return y + 1;
				}
			}

			return 0;
		}
		
		public void CalculateHeight(bool doLighting = true)
		{
			for (int x = 0; x < 16; x++)
			{
				for (int z = 0; z < 16; z++)
				{
					RecalculateHeight(x, z, doLighting);
				}
			}

			GetHeighest();

			foreach (var section in Sections)
			{
				section?.RemoveInvalidBlocks();
			}
		}

		private static BlockState Air = BlockFactory.GetBlockState("minecraft:air");

		public IEnumerable<ChunkSection.BlockEntry> GetBlockStates(int bx, int by, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
			{
				yield return new ChunkSection.BlockEntry(Air, 0);
				yield break;
			}

			var chunk = Sections[by >> 4];
			if (chunk == null)
			{
				yield return new ChunkSection.BlockEntry(Air, 0);
				yield break;
			}
			
			foreach (var bs in chunk.GetAll(bx, by - 16 * (by >> 4), bz))
			{
				yield return bs;
			}
		}

		public BlockState GetBlockState(int bx, int by, int bz)
		{
			return GetBlockState(bx, by, bz, 0);
		}

		public BlockState GetBlockState(int bx, int by, int bz, int storage)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return Air;

			var chunk = Sections[by >> 4];
			if (chunk == null) return Air;

			return chunk.Get(bx, by - 16 * (by >> 4), bz, storage);
		}

		public void SetHeight(int bx, int bz, short h)
		{
			if ((bx < 0 || bx > ChunkWidth) || (bz < 0 || bz > ChunkDepth))
				return;

			Height[((bz << 4) + (bx))] = h;
		}

		public byte GetHeight(int bx, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (bz < 0 || bz > ChunkDepth))
				return 255;

			return (byte) Height[((bz << 4) + (bx))];
		}

		public void SetBiome(int bx, int by, int bz, int biome)
		{
			if ((bx < 0 || bx > ChunkWidth) || (bz < 0 || bz > ChunkDepth))
				return;

			BiomeId[(by << 8 | bz << 4 | bx)] = biome;
		}

		public int GetBiome(int bx, int by, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (bz < 0 || bz > ChunkDepth))
				return 0;

			return BiomeId[(by << 8 | bz << 4 | bx)];
		}

		public byte GetBlocklight(int bx, int by, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return 0;

			var section = Sections[by >> 4];
			if (section == null) return 0;

			return (byte) section.GetBlocklight(bx, by - 16 * (by >> 4), bz);
		}

		public void SetBlocklight(int bx, int by, int bz, byte data)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return;
			
			GetSection(by).SetBlocklight(bx, by - 16 * (by >> 4), bz, data);
		}

		public byte GetSkylight(int bx, int by, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return 0xff;

			var section = Sections[by >> 4];
			if (section == null) return 0xff;

			return section.GetSkylight(bx, by - 16 * (by >> 4), bz);
		}

		public bool SetSkyLight(int bx, int by, int bz, byte data)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return false;

			bool dirty = GetSection(by).SetSkylight(bx, by - 16 * (by >> 4), bz, data);
			return dirty;
		}
		
	//	public NbtCompound[] Entities { get; internal set; }

		public bool HasDirtySubChunks
		{
			get { return Sections != null && Sections.Any(s => s != null && s.IsDirty); }
		}
		
		private bool _heightDirty = true;
		private int _heighest = 256;

		public int GetHeighest()
		{
			if (_heightDirty)
			{
				_heighest = Height.Max();
				_heightDirty = false;
			}

			return _heighest;
		}

		public void ScheduleBlockUpdate(int x, int y, int z)
		{
			if ((x < 0 || x > ChunkWidth) || (y < 0 || y > ChunkHeight) || (z < 0 || z > ChunkDepth))
				return;

			var section = Sections[y >> 4];
			if (section == null) return;
			section.SetScheduled(x, y - 16 * (y >> 4), z, true);
		}

		public bool IsScheduled(int bx, int @by, int bz)
		{
			if ((bx < 0 || bx > ChunkWidth) || (by < 0 || by > ChunkHeight) || (bz < 0 || bz > ChunkDepth))
				return false;

			var section = Sections[@by >> 4];
			if (section == null) return false;

			return section.IsScheduled(bx, @by & 0xf, bz);
		}
		
		public bool AddBlockEntity(BlockCoordinates coordinates, BlockEntity entity)
		{
			entity.Block = GetBlockState(coordinates.X, coordinates.Y, coordinates.Z).Block;
			return BlockEntities.TryAdd(coordinates, entity);
		}

		public bool TryGetBlockEntity(BlockCoordinates coordinates, out BlockEntity entity)
		{
			return BlockEntities.TryGetValue(coordinates, out entity);
		}
	    
		public bool RemoveBlockEntity(BlockCoordinates coordinates)
		{
			return BlockEntities.TryRemove(coordinates, out _);
		}

		public void Dispose()
		{
			lock (_dataLock)
			{
				foreach (var chunksSection in Sections)
				{
					chunksSection?.Dispose();
				}

				ChunkData?.Dispose();
				ChunkData = null;
			}
		}
	}
}
