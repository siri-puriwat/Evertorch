using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class RenderPipelineSettingsTests
{
    [Test]
    public void DefaultRenderPipeline_InProjectSettings_IsUniversalRenderPipeline()
    {
        Assert.That(GraphicsSettings.defaultRenderPipeline, Is.InstanceOf<UniversalRenderPipelineAsset>());
    }
}
}
