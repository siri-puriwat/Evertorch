using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Entity bodies come from Addressables by their content key; in the editor that reads the Asset Database.
/// </summary>
public sealed class EntityViewCatalogTests
{
    private const int FrameLimit = 300;

    private EntityViewCatalog? m_catalog;
    private EntityView? m_view;

    [TearDown]
    public void TearDown()
    {
        if (m_view != null)
        {
            Object.DestroyImmediate(m_view.gameObject);
        }

        m_catalog?.Dispose();
    }

    [UnityTest]
    public IEnumerator Create_ForAKnownKey_AttachesThePrefabWithoutColliders()
    {
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Slime", "monster_training_slime", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.HasBody, Is.True);
        Assert.That(m_view.IsPlaceholder, Is.False);
        Assert.That(m_view.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty);
        Assert.That(m_view.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Create_ForAnUnknownKey_ShowsAPlaceholderAndWarns()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'no_such_view'"));
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Unknown", "no_such_view", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.HasBody, Is.True);
        Assert.That(m_view.IsPlaceholder, Is.True);
        Assert.That(m_view.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    private static IEnumerator WaitForBody(EntityView view)
    {
        for (int frame = 0; frame < FrameLimit && !view.HasBody; frame++)
        {
            yield return null;
        }
    }
}
}
