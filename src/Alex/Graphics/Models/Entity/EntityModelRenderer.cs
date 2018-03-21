﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Alex.API.Graphics;
using Alex.Rendering.Camera;
using Alex.ResourcePackLib.Json.Models;
using Alex.ResourcePackLib.Json.Models.Entities;
using Alex.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NLog;

namespace Alex.Graphics.Models.Entity
{
	public class EntityModelRenderer : Model
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(EntityModelRenderer));
		private static ConcurrentDictionary<string, VertexPositionNormalTexture[]> ModelBonesCache { get; } = new ConcurrentDictionary<string, VertexPositionNormalTexture[]>();

		private EntityModel Model { get; }
		private IReadOnlyDictionary<string, ModelPart> Cubes { get; }
		private Texture2D Texture { get; set; }
		//private AlphaTestEffect TransparentEffect { get; set; }
		public EntityModelRenderer(EntityModel model, Texture2D texture)
		{
			Model = model;
			Texture = texture;

			var cubes = new Dictionary<string, ModelPart>();
			Cache(cubes);

			Cubes = cubes;
		}

		static EntityModelRenderer()
		{
			
		}

		private void Cache(Dictionary<string, ModelPart> cubes)
		{
			float x = 0, y = 0, z = 0;
		//	List<VertexPositionNormalTexture> textures = new List<VertexPositionNormalTexture>();
			foreach (var bone in Model.Bones)
			{
				if (bone == null) continue;
				if (bone.NeverRender) continue;

				if (bone.Cubes != null)
				{
					foreach (var cube in bone.Cubes)
					{
						if (cube == null)
						{
							Log.Warn("Cube was null!");
							continue;
						}

						var size = cube.Size;
						var origin = cube.Origin;
						var pivot = bone.Pivot;
						var rotation = bone.Rotation;

						VertexPositionNormalTexture[] vertices = ModelBonesCache.GetOrAdd($"{Model.Name}:{bone.Name}", s =>
						{
							Cube built = new Cube(size, new Vector2(Texture.Width, Texture.Height));
							built.BuildCube(cube.Uv);

							return built.Front.Concat(built.Back).Concat(built.Top).Concat(built.Bottom).Concat(built.Left)
								.Concat(built.Right).ToArray();
						});

						var part = new ModelPart(vertices,
							Texture,
							rotation, pivot, origin);
						if (!cubes.TryAdd(bone.Name, part))
						{
							part.Dispose();
							Log.Warn($"Failed to add cube to list of bones: {Model.Name}:{bone.Name}");
						}
					}
				}
			}
		}

		public void Render(IRenderArgs args, Camera camera, Vector3 position, float yaw, float pitch)
		{
			foreach (var bone in Cubes)
			{
				if (bone.Value.IsDirty)
					continue;


				bone.Value.Render(args, camera, position, yaw, bone.Key.Contains("head") ? pitch : 0);
			}
		}

		public void Update(GraphicsDevice device, GameTime gameTime, Vector3 position, float yaw, float pitch)
		{
			foreach (var bone in Cubes)
			{
				if (!bone.Value.IsDirty)
					continue;
				
				bone.Value.Update(device, gameTime);
			}
		}

		public override string ToString()
		{
			return Model.Name;
		}

		private class ModelPart : IDisposable
		{
			private AlphaTestEffect Effect { get; set; }
			private VertexBuffer Buffer { get; set; }
			private VertexPositionNormalTexture[] _vertices;

			public bool IsDirty { get; private set; }
			public Texture2D Texture { get; private set; }

			public Vector3 Rotation { get; private set; } = Vector3.Zero;
			public Vector3 Pivot { get; private set; } = Vector3.Zero;
			public Vector3 Origin { get; private set; }
			public ModelPart(VertexPositionNormalTexture[] textures, Texture2D texture, Vector3 rotation, Vector3 pivot, Vector3 origin)
			{
				_vertices = textures;
				Texture = texture;
				Rotation = rotation;
				Pivot = pivot;
				Origin = origin;

				Mod(ref _vertices, Origin, Pivot, Rotation);

				IsDirty = true;
			}

			public void Update(GraphicsDevice device, GameTime gameTime)
			{
				if (!IsDirty) return;

				//if (_vertices.Length > 0)
				//	Mod(ref _vertices, Origin, Pivot, Rotation);

				if (Effect == null)
				{
					Effect = new AlphaTestEffect(device);
					Effect.Texture = Texture;
				}

				VertexBuffer currentBuffer = Buffer;
				if (_vertices.Length > 0 && (Buffer == null || currentBuffer.VertexCount != _vertices.Length))
				{
					if (currentBuffer == null)
					{
						Buffer = new VertexBuffer(device,
							VertexPositionNormalTexture.VertexDeclaration, _vertices.Length, BufferUsage.WriteOnly);
						currentBuffer = Buffer;
						currentBuffer.SetData(_vertices);
					}
					else if (_vertices.Length > currentBuffer.VertexCount)
					{
						VertexBuffer oldBuffer = currentBuffer;

						currentBuffer = new VertexBuffer(device, VertexPositionNormalTextureColor.VertexDeclaration, _vertices.Length, BufferUsage.WriteOnly);
						currentBuffer.SetData(_vertices);

						Buffer = currentBuffer;
						oldBuffer.Dispose();
					}
					else
					{
						currentBuffer.SetData(_vertices);
					}
				}

				IsDirty = false;
			}

			public void Render(IRenderArgs args, Camera camera, Vector3 position, float yaw, float pitch)
			{
				if (_vertices == null || _vertices.Length == 0) return;

				if (Effect == null || Buffer == null) return;

				var buffer = Buffer;

				Effect.World = Matrix.CreateScale(1f / 16f) * Matrix.CreateRotationY(MathUtils.ToRadians(yaw)) * Matrix.CreateRotationX(MathUtils.ToRadians(pitch)) * Matrix.CreateTranslation(position);
				//Effect.World = Matrix.CreateScale(1f / 16f) * Matrix.CreateTranslation(position);

				Effect.View = camera.ViewMatrix;
				Effect.Projection = camera.ProjectionMatrix;

				args.GraphicsDevice.SetVertexBuffer(buffer);
				foreach (var pass in Effect.CurrentTechnique.Passes)
				{
					pass.Apply();
				}

				args.GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, _vertices.Length / 3);
			}

			private void Mod(ref VertexPositionNormalTexture[] data, Vector3 origin, Vector3 pivot, Vector3 rotation)
			{
				Matrix transform = Matrix.CreateTranslation(-pivot) *
					Matrix.CreateRotationX(rotation.X) *
					Matrix.CreateRotationY(rotation.Y) *
					Matrix.CreateRotationZ(rotation.Z) *
					Matrix.CreateTranslation(pivot);

				for (int i = 0; i < data.Length; i++)
				{
					var pos = data[i].Position;

					pos = pos + origin;

					if (rotation != Vector3.Zero)
					{
						pos = Vector3.Transform(pos, transform);
					}

					data[i].Position = pos;
				}
			}

			public void Dispose()
			{
				IsDirty = false;
				Effect?.Dispose();
				Buffer?.Dispose();
			}
		}
	}
}
