using System;
using System.Collections.Generic;
using LiquidGlassWinUI.Interop;

namespace LiquidGlassWinUI.Effects
{
    // Optional pointer-following reflection applied AFTER the stable liquid-glass
    // shader. Keeping this in a separate flattened pass is intentional: the main
    // LiquidGlass shader is already close to DWM's shader-linking budget, and adding
    // the reflection math there can make DWM silently discard the whole material.
    internal sealed class ShimmerEffect : CustomEffectBase
    {
        public const string EffectNameValue = "LiquidGlassShimmerEffect";

        public const string XPropertyPath = EffectNameValue + ".ShimmerX";
        public const string YPropertyPath = EffectNameValue + ".ShimmerY";
        public const string StrengthPropertyPath = EffectNameValue + ".ShimmerStrength";
        public const string RadiusPropertyPath = EffectNameValue + ".ShimmerRadius";
        public const string DprPropertyPath = EffectNameValue + ".Dpr";

        private const uint CbufferSizeBytes = 32;

        protected override Guid Id =>
            new("7a2f4d9c-4f1b-47e8-9c6a-0e3b8d1f52a7");

        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "Shimmer.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource
            {
                Name = "Source",
                WantsSamplerDataExt = true,
            },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
        };

        protected override ushort LinkingArgType =>
            CustomEffectInterop.LinkingArgCustomSamplerResult;

        // Flatten the completed glass into an intermediate texture. DWM then links
        // this deliberately small shader independently instead of growing the main
        // glass fragment beyond its reliable instruction budget.
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";

        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty { PublicName = "ShimmerX",        NativeName = "ShimmerX",        CbufferOffset = 0,  DefaultValue = 0.5f },
            new EffectProperty { PublicName = "ShimmerY",        NativeName = "ShimmerY",        CbufferOffset = 4,  DefaultValue = 0.5f },
            new EffectProperty { PublicName = "ShimmerStrength", NativeName = "ShimmerStrength", CbufferOffset = 8,  DefaultValue = 0.0f },
            new EffectProperty { PublicName = "ShimmerRadius",   NativeName = "ShimmerRadius",   CbufferOffset = 12, DefaultValue = 104.0f },
            new EffectProperty { PublicName = "Dpr",             NativeName = "Dpr",             CbufferOffset = 16, DefaultValue = 1.0f },
        };

        protected override byte[] ConstantBuffer
        {
            get
            {
                float[] values =
                {
                    0.5f, 0.5f, 0.0f, 104.0f,
                    1.0f, 0.0f, 0.0f, 0.0f,
                };
                byte[] bytes = new byte[CbufferSizeBytes];
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                return bytes;
            }
        }
    }
}
