using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A weapon's model in a hand (Prototype Content §2): its grip picks the attack clip a body swings with it.
/// </summary>
public sealed class HeldWeapon : MonoBehaviour
{
    [SerializeField]
    private WeaponGrip m_grip;

    public WeaponGrip Grip => m_grip;

    public void Configure(WeaponGrip grip)
    {
        m_grip = grip;
    }
}
}
