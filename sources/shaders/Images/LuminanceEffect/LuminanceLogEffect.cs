﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
namespace SiliconStudio.Paradox.Effects.Images
{
    /// <summary>
    /// Class LuminanceLogEffect.
    /// </summary>
    public class LuminanceLogEffect : ImageEffectShader
    {
        public LuminanceLogEffect(DrawEffectContext context, string luminanceShaderName = "LuminanceLogShader") : base(context, luminanceShaderName)
        {
        }
    }
}