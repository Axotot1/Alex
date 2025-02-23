#if OPENGL
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 World;
float4x4 Projection;
float4x4 View;

float4 AlphaTest;
float4 LightOffset;

SamplerState textureSampler
{
    Texture = <Texture>;
    MipFilter = Point;
    MagFilter = Point;
    MinFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

struct VertexToPixel  {
    float4 Position     : POSITION;
    float4 BlockLight   : TEXCOORD01;
    float4 SkyLight     : TEXCOORD02;
};

struct PixelToFrame  {
    float4 Color        : SV_Target;
};

VertexToPixel VertexShaderFunction(float4 inPosition : POSITION, float4 inTexCoords : TEXCOORD0, float4 inColor : COLOR0, float4 blockLight : TEXCOORD01, float4 skyLight : TEXCOORD02)  {
    VertexToPixel Output = (VertexToPixel)0;

    float4 worldPos = mul(inPosition, World);
    float4 viewPos = mul(worldPos, View);

    Output.Position = mul(viewPos, Projection);
    Output.BlockLight = blockLight;
    Output.SkyLight = skyLight;

    return Output;
}

PixelToFrame PixelShaderFunction(VertexToPixel PSIn)  {
    PixelToFrame Output = (PixelToFrame)0;

    float4 baseColor = 0.086f;
    float4 modifiedSkyLight = clamp(PSIn.SkyLight * LightOffset, 0, 15);

    float4 lighting = max(PSIn.BlockLight, modifiedSkyLight);
    
    float4 colorValue = pow((1.0f / 16.0f) * lighting, 1.4f) + baseColor;

    Output.Color = float4(1,1,1,1);

    Output.Color.r *= colorValue;
    Output.Color.g *= colorValue;
    Output.Color.b *= colorValue;

    clip((Output.Color.a < AlphaTest.x) ? AlphaTest.z : AlphaTest.w);

    return Output;
}

technique Block  {
    pass Pass0  {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}