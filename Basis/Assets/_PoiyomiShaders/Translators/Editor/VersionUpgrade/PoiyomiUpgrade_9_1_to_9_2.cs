using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poi.Tools.ShaderTranslator.VersionUpgrade
{
	public class PoiyomiUpgrade_9_1_to_9_2 : ScriptedShaderTranslator, IPoiyomiVersionUpgrade
	{
		public static readonly Version SourceVersion = new Version(9, 1);
		public static readonly Version TargetVersion = new Version(9, 2);

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
			string targetShaderName = $".poiyomi/Old Versions/{TargetVersion.Major}.{TargetVersion.Minor}/{variant}";

			Shader targetShader = Shader.Find(targetShaderName);
			if (targetShader != null)
				return targetShader;

			return base.GetTargetShader(sourceMaterial, newShaderName);
		}

		protected override List<PropertyTranslation> AddProperties()
		{
			// Placeholder - add property translations as needed when changes are identified
			return new List<PropertyTranslation>();
		}

		protected override void DoAfterTranslation(TranslationContext context)
		{
			SetTargetRenderQueue(context, context.originalRenderQueue);
		}
	}
}
