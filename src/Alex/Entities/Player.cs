﻿using System;
using System.Collections.Generic;
using System.Linq;
using Alex.API.Entities;
using Alex.API.Graphics;
using Alex.API.Input;
using Alex.API.Network;
using Alex.API.Utils;
using Alex.API.World;
using Alex.Blocks.Minecraft;
using Alex.GameStates.Playing;
using Alex.Items;
using Alex.Utils;
using Alex.Worlds;
using Alex.Worlds.Bedrock;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MiNET;
using MiNET.Net;
using MiNET.Utils;
using NLog;
using BlockCoordinates = Alex.API.Utils.BlockCoordinates;
using ChunkCoordinates = Alex.API.Utils.ChunkCoordinates;
using ContainmentType = Microsoft.Xna.Framework.ContainmentType;
using IBlockState = Alex.API.Blocks.State.IBlockState;
using Inventory = Alex.Utils.Inventory;
using Skin = Alex.API.Utils.Skin;

namespace Alex.Entities
{
    public class Player : PlayerMob
    {
	    private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(Player));

        public static readonly float EyeLevel = 1.625F;
        public static readonly float Height = 1.8F;

		public PlayerIndex PlayerIndex { get; }

		public float FOVModifier { get; set; } = 0;

		public PlayerController Controller { get; }
        public Vector3 Raytraced = Vector3.Zero;
        public Vector3 AdjacentRaytrace = Vector3.Zero;
        public bool HasAdjacentRaytrace = false;
        public bool HasRaytraceResult = false;

        public int Health { get; set; } = 20;
        public int MaxHealth { get; set; } = 20;

        public int Hunger { get; set; } = 19;
        public int MaxHunger { get; set; } = 20;

        public int Saturation { get; set; } = 0;
        public int MaxSaturation { get; set; }
        
        public int Exhaustion { get; set; } = 0;
        public int MaxExhaustion { get; set; }
        
        public bool IsWorldImmutable { get; set; } = false;
        public bool IsNoPvP { get; set; } = true;
        public bool IsNoPvM { get; set; } = true;
        
        private World World { get; }
        public Player(GraphicsDevice graphics, InputManager inputManager, string name, World world, Skin skin, INetworkProvider networkProvider, PlayerIndex playerIndex) : base(name, world, networkProvider, skin.Texture)
        {
	        World = world;
		//	DoRotationCalculations = false;
			PlayerIndex = playerIndex;
		    Controller = new PlayerController(graphics, world, inputManager, this, playerIndex); 
		    NoAi = false;

			//Inventory = new Inventory(46);
			//Inventory.SelectedHotbarSlotChanged += SelectedHotbarSlotChanged;
			//base.Inventory.IsPeInventory = true;
			MovementSpeed = 4.317f;
			FlyingSpeed = 10.89f;

			SnapHeadYawRotationOnMovement = false;

			RenderEntity = true;
			ShowItemInHand = true;
		}

        protected override void OnInventorySlotChanged(object sender, SlotChangedEventArgs e)
        {
	        //Crafting!
	        if (e.Index >= 41 && e.Index <= 44)
	        {
		        McpeInventoryTransaction transaction = McpeInventoryTransaction.CreateObject();
		        transaction.transaction = new NormalTransaction()
		        {
			        TransactionRecords = new List<TransactionRecord>()
			        {
				        new CraftTransactionRecord()
				        {
					        Action = McpeInventoryTransaction.CraftingAction.CraftAddIngredient,
					        Slot = e.Index,
					        NewItem = BedrockClient.GetMiNETItem(e.Value),
					        OldItem = BedrockClient.GetMiNETItem(e.OldItem)
				        }
			        }
		        };
	        }
	        
	        base.OnInventorySlotChanged(sender, e);
        }

        public bool IsBreakingBlock => _destroyingBlock;

	    public float BlockBreakProgress
	    {
		    get
		    {
			    if (!IsBreakingBlock)
				    return 0;
			    
			    var end = DateTime.UtcNow;
			    var start = _destroyingTick;

			    var timeRan = (end - start).TotalMilliseconds / 50d;

			    return (float) ((1f / (float) _destroyTimeNeeded) * timeRan);
		    }
	    }

	    public double BreakTimeNeeded
	    {
		    set
		    {
			    _destroyTimeNeeded = value;
		    }
	    }

	    public bool WaitingOnChunk { get; set; } = false;
	    
	    public BlockCoordinates TargetBlock => _destroyingTarget;

	    private BlockCoordinates _destroyingTarget = BlockCoordinates.Zero;
	    private bool _destroyingBlock = false;
        private DateTime _destroyingTick = DateTime.MaxValue;
	    private double _destroyTimeNeeded = 0;
	    private BlockFace _destroyingFace;
	    
	    private int PreviousSlot { get; set; } = 9;
	    private DateTime _lastTimeWithoutInput = DateTime.MinValue;
	    private bool _prevCheckedInput = false;
	    public override void Update(IUpdateArgs args)
		{
			if (WaitingOnChunk && Age % 4 == 0)
			{
				NoAi = true;
				
				if (Level.GetChunk(KnownPosition.GetCoordinates3D(), true) != null)
				{
					Velocity = Vector3.Zero;
					WaitingOnChunk = false;
					NoAi = false;
				}
			}

			ChunkCoordinates oldChunkCoordinates = new ChunkCoordinates(base.KnownPosition);
			bool sprint = IsSprinting;
			bool sneak = IsSneaking;

			if (!CanFly && IsFlying)
				IsFlying = false;
			
			Controller.Update(args.GameTime);
			//KnownPosition.HeadYaw = KnownPosition.Yaw;

			if (IsSprinting && !sprint)
			{
				FOVModifier += 10;
				
				Network.EntityAction((int) EntityId, EntityAction.StartSprinting);
			}
			else if (!IsSprinting && sprint)
			{
				FOVModifier -= 10;
				
				Network.EntityAction((int)EntityId, EntityAction.StopSprinting);
			}

			if (IsSneaking != sneak)
			{
				if (IsSneaking)
				{
					Network.EntityAction((int)EntityId, EntityAction.StartSneaking);		
				}
				else
				{
					Network.EntityAction((int)EntityId, EntityAction.StopSneaking);
				}
			}

			var previousCheckedInput = _prevCheckedInput;
			
			if ((Controller.CheckInput && Controller.CheckMovementInput))
			{
				_prevCheckedInput = true;
				if (!previousCheckedInput || World.FormManager.IsShowingForm)
				{
					return;
				}
				
				UpdateRayTracer();
				
				var hitEntity = HitEntity;
				if (hitEntity != null && Controller.InputManager.IsPressed(InputCommand.LeftClick))
				{
					if (_destroyingBlock)
						StopBreakingBlock(forceCanceled:true);
					
					InteractWithEntity(hitEntity, true);
				}
				else if (hitEntity != null && Controller.InputManager.IsPressed(InputCommand.RightClick))
				{
					if (_destroyingBlock)
						StopBreakingBlock(forceCanceled:true);
					
					InteractWithEntity(hitEntity, false);
				}
				else if (hitEntity == null && !_destroyingBlock && Controller.InputManager.IsDown(InputCommand.LeftClick) && !IsWorldImmutable) //Destroying block.
				{
					StartBreakingBlock();
				}
				else if (_destroyingBlock && Controller.InputManager.IsUp(InputCommand.LeftClick))
				{
					StopBreakingBlock();
				}
				else if (_destroyingBlock && Controller.InputManager.IsDown(InputCommand.LeftClick))
				{
					if (_destroyingTarget != new BlockCoordinates(Vector3.Floor(Raytraced)))
					{
						StopBreakingBlock(true, true);

						if (Gamemode != Gamemode.Creative)
						{
							StartBreakingBlock();
						}
					}
					else
					{
						var timeRan = (DateTime.UtcNow - _destroyingTick).TotalMilliseconds / 50d;
						if (timeRan >= _destroyTimeNeeded)
						{
							StopBreakingBlock(true);
						}
					}
				}
				else if (Controller.InputManager.IsPressed(InputCommand.RightClick))
				{
					bool handledClick = false;
					var item = Inventory[Inventory.SelectedSlot];
					// Log.Debug($"Right click!");
					if (item != null)
					{
						handledClick = HandleRightClick(item, Inventory.SelectedSlot);
					}

					/*if (!handledClick && Inventory.OffHand != null && !(Inventory.OffHand is ItemAir))
					{
						handledClick = HandleRightClick(Inventory.OffHand, 1);
					}*/
				}
            }
			else
			{
				if (_destroyingBlock)
				{
					StopBreakingBlock();
				}

				_prevCheckedInput = false;
				_lastTimeWithoutInput = DateTime.UtcNow;
			}

			if (PreviousSlot != Inventory.SelectedSlot)
			{
				var slot = Inventory.SelectedSlot;
				Network?.HeldItemChanged(Inventory[Inventory.SelectedSlot], (short) slot);
				PreviousSlot = slot;
			}

			base.Update(args);

		}

	    private void InteractWithEntity(IEntity entity, bool attack)
	    {
		    bool canAttack = true;

		    if (entity is PlayerMob)
		    {
			    canAttack = !IsNoPvP && Level.Pvp;
		    }
		    else
		    {
			    canAttack = !IsNoPvM;
		    }

		  //  Log.Info($"Interacting with entity. Attack: {attack} - CanAttack: {canAttack} - PVM: {IsNoPvM} - PVP: {IsNoPvP}");
		    
		    if (attack)
		    {
			   // entity.EntityHurt();
			    Network?.EntityInteraction(this, entity, McpeInventoryTransaction.ItemUseOnEntityAction.Attack);
		    }
		    else
		    {
			    Network?.EntityInteraction(this, entity, McpeInventoryTransaction.ItemUseOnEntityAction.Interact);
		    }
	    }

	    public IEntity HitEntity { get; private set; } = null;
	    public IEntity[] EntitiesInRange { get; private set; } = null;

	    private void UpdateRayTracer()
	    {
		    var camPos = Level.Camera.Position;
		    var lookVector = Level.Camera.Direction;

		    var entities = Level.EntityManager.GetEntities(camPos, 8);
		    EntitiesInRange = entities.ToArray();

		    if (EntitiesInRange.Length == 0)
		    {
			    HitEntity = null;
			    return;
		    }
		    
		    IEntity hitEntity = null;
		    for (float x = 0.5f; x < 8f; x += 0.1f)
		    {
			    Vector3 targetPoint = camPos + (lookVector * x);
			    var entity = EntitiesInRange.FirstOrDefault(xx =>
				    xx.GetBoundingBox().Contains(targetPoint) == ContainmentType.Contains);

			    if (entity != null)
			    {
				    hitEntity = entity;
				    break;
			    }
		    }

		    HitEntity = hitEntity;
	    }

	    private void BlockBreakTick()
	    {
		    //_destroyingTick++;
        }

	    private void StartBreakingBlock()
	    {
			var floored =  Vector3.Floor(Raytraced);

		    var block = Level.GetBlock(floored);
		    if (!block.HasHitbox)
		    {
			    return;
		    }

            _destroyingBlock = true;
		    _destroyingTarget = floored;
		    _destroyingFace = GetTargetFace();
		    _destroyingTick = DateTime.UtcNow;

		    //if (Inventory.MainHand != null)
		    {
			    _destroyTimeNeeded = block.GetBreakTime(Inventory.MainHand ?? new ItemAir()) * 20f;
		    }

            Log.Debug($"Start break block ({_destroyingTarget}, {_destroyTimeNeeded} ticks.)");

            var flooredAdj = Vector3.Floor(AdjacentRaytrace);
            var remainder = new Vector3(AdjacentRaytrace.X - flooredAdj.X, AdjacentRaytrace.Y - flooredAdj.Y, AdjacentRaytrace.Z - flooredAdj.Z);

            Network?.PlayerDigging(DiggingStatus.Started, _destroyingTarget, _destroyingFace, remainder);
        }

	    private void StopBreakingBlock(bool sendToServer = true, bool forceCanceled = false)
	    {
		    var end = DateTime.UtcNow;
		    _destroyingBlock = false;
           // var ticks = Interlocked.Exchange(ref _destroyingTick, 0);// = 0;
		    var start = _destroyingTick;
			_destroyingTick = DateTime.MaxValue;

		    var timeRan = (end - start).TotalMilliseconds / 50d;

            var flooredAdj = Vector3.Floor(AdjacentRaytrace);
            var remainder = new Vector3(AdjacentRaytrace.X - flooredAdj.X, AdjacentRaytrace.Y - flooredAdj.Y, AdjacentRaytrace.Z - flooredAdj.Z);

            if (!sendToServer)
		    {
			    Log.Debug($"Stopped breaking block, not notifying server. Time: {timeRan}");
                return;
		    }

		    if ((Gamemode == Gamemode.Creative  || timeRan >= _destroyTimeNeeded) && !forceCanceled)
		    {
                Network?.PlayerDigging(DiggingStatus.Finished, _destroyingTarget, _destroyingFace, remainder);
			    Log.Debug($"Stopped breaking block. Ticks passed: {timeRan}");

				Level.SetBlockState(_destroyingTarget, new Air().GetDefaultState());
            }
		    else
		    {
			    Network?.PlayerDigging(DiggingStatus.Cancelled, _destroyingTarget, _destroyingFace, remainder);
			    Log.Debug($"Cancelled breaking block. Tick passed: {timeRan}");
            }
	    }

	    private BlockFace GetTargetFace()
	    {
		    var flooredAdj =  Vector3.Floor(AdjacentRaytrace);
		    var raytraceFloored  = Vector3.Floor(Raytraced);

		    var adj = flooredAdj - raytraceFloored;
		    adj.Normalize();

		    return adj.GetBlockFace();
        }

	    private bool HandleRightClick(Item slot, int hand)
	    {
		    //if (ItemFactory.ResolveItemName(slot.ItemID, out string itemName))
		    {
			    var flooredAdj = Vector3.Floor(AdjacentRaytrace);
			    var raytraceFloored = Vector3.Floor(Raytraced);

			    var adj = flooredAdj - raytraceFloored;
			    adj.Normalize();

			    var face = adj.GetBlockFace();

			    var remainder = new Vector3(AdjacentRaytrace.X - flooredAdj.X,
				    AdjacentRaytrace.Y - flooredAdj.Y, AdjacentRaytrace.Z - flooredAdj.Z);

			    var coordR = new BlockCoordinates(raytraceFloored);
			    
			    //IBlock block = null;
			    if (!IsWorldImmutable)
			    {
				    var existingBlock = Level.GetBlock(coordR);
				    bool isBlockItem = slot is ItemBlock;
				    
				    if (existingBlock.CanInteract && (!isBlockItem || IsSneaking))
				    {
					    Network?.WorldInteraction(coordR, face, hand, remainder);

					    return true;
				    }
				    
				    if (slot is ItemBlock ib)
				    {
					    IBlockState blockState = ib.Block;

					    if (blockState != null && !(blockState.Block is Air) && HasRaytraceResult)
					    {
						    if (existingBlock.IsReplacible || !existingBlock.Solid)
						    {
							    if (CanPlaceBlock(coordR, (Block) blockState.Block))
							    {
								    Level.SetBlockState(coordR, blockState);

								    Network?.BlockPlaced(coordR.BlockDown(), BlockFace.Up, hand, remainder, this);

								    return true;
							    }
						    }
						    else
						    {
							    var target = new BlockCoordinates(raytraceFloored + adj);
							    if (CanPlaceBlock(target, (Block) blockState.Block))
							    {
								    Level.SetBlockState(target, blockState);

								    Network?.BlockPlaced(coordR, face, hand, remainder, this);
								    
								    return true;
							    }
						    }
					    }
				    }
			    }

			    if (!(slot is ItemAir) && slot.Id > 0 && slot.Count > 0)
                {
                    Network?.UseItem(slot, hand);
                    Log.Debug($"Used item!");

	                return true;
                }
            }

		    return false;
	    }

	    private bool CanPlaceBlock(BlockCoordinates coordinates, Block block)
	    {
		    var bb = block.GetBoundingBox(coordinates);
		    var playerBb = GetBoundingBox(KnownPosition);

		    if (playerBb.Intersects(bb))
		    {
			    return false;
		    }

		    return true;
	    }

		public override void TerrainCollision(Vector3 collisionPoint, Vector3 direction)
		{
		//	Log.Debug($"Terrain collision: {collisionPoint.ToString()} | {direction}");	
			base.TerrainCollision(collisionPoint, direction);
		}

		public override void OnTick()
		{
			if (_destroyingBlock)
			{
				BlockBreakTick();
			}

			base.OnTick();
		}
	}
}