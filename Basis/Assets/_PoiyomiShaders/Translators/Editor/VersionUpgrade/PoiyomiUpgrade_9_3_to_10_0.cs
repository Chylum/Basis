using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poi.Tools.ShaderTranslator.VersionUpgrade
{
	public class PoiyomiUpgrade_9_3_to_10_0 : ScriptedShaderTranslator, IPoiyomiVersionUpgrade
	{
		public static readonly Version SourceVersion = new Version(9, 3);
		public static readonly Version TargetVersion = new Version(10, 0);

		public Version GetSourceVersion() => SourceVersion;
		public Version GetTargetVersion() => TargetVersion;

		public override bool CanTranslateMaterial(Material sourceMaterial)
		{
			if (!PoiyomiVersionDetector.IsPoiyomiShader(sourceMaterial))
				return false;

			if (!PoiyomiVersionDetector.TryGetVersion(sourceMaterial, out Version version))
				return false;

			return version.Major == SourceVersion.Major && version.Minor == SourceVersion.Minor;
		}

		protected override Shader GetTargetShader(Material sourceMaterial, string newShaderName)
		{
			Shader effectiveShader = PoiyomiVersionDetector.GetEffectiveShader(sourceMaterial);
			string variant = PoiyomiVersionDetector.GetShaderVariant(effectiveShader);

			// 10.0 is the latest version, so use the main shader path (not Old Versions)
			string targetShaderName = $".poiyomi/{variant}";

			Shader targetShader = Shader.Find(targetShaderName);
			if (targetShader != null)
				return targetShader;

			return base.GetTargetShader(sourceMaterial, newShaderName);
		}

		protected override List<PropertyTranslation> AddProperties()
		{
			return new List<PropertyTranslation>
			{
				// Flipbook positioning: _FlipbookScaleOffset (sX, sY, oX, oY) -> _FlipbookPosition + _FlipbookScale
				new PropertyTranslation("_FlipbookScaleOffset", (prop, ctx) =>
				{
					Vector4 scaleOffset = GetSourcePropertyValue<Vector4>(ctx, prop);
					// Old: sX, sY = scale, oX, oY = offset from center
					// New: Position is center (0.5, 0.5) + offset, Scale is separate
					Vector2 position = new Vector2(scaleOffset.z + 0.5f, scaleOffset.w + 0.5f);
					Vector3 scale = new Vector3(scaleOffset.x, scaleOffset.y, 1f);
					SetTargetPropertyValue(ctx, "_FlipbookPosition", position);
					SetTargetPropertyValue(ctx, "_FlipbookScale", scale);
				}),

				// Rim Lighting: _RimSharpness -> _RimBlur (inverted: blur = width - sharpness)
				new PropertyTranslation("_RimSharpness", (prop, ctx) =>
				{
					float sharpness = GetSourcePropertyValue<float>(ctx, prop);
					float width = GetSourcePropertyValue<float>(ctx, "_RimWidth");
					float blur = Mathf.Clamp01(width - sharpness);
					SetTargetPropertyValue(ctx, "_RimBlur", blur);
				}),

				// Rim Lighting 2: _Rim2Sharpness -> _Rim2Blur
				new PropertyTranslation("_Rim2Sharpness", (prop, ctx) =>
				{
					float sharpness = GetSourcePropertyValue<float>(ctx, prop);
					float width = GetSourcePropertyValue<float>(ctx, "_Rim2Width");
					float blur = Mathf.Clamp01(width - sharpness);
					SetTargetPropertyValue(ctx, "_Rim2Blur", blur);
				}),
			};
		}

		protected override void DoAfterTranslation(TranslationContext context)
		{
			SetTargetRenderQueue(context, context.originalRenderQueue);
		}
	}
}
