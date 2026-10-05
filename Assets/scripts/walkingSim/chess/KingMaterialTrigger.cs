using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class KingMaterialTrigger : MonoBehaviour
{
    [Tooltip("Only the owning cube's body renderer changes, not its children.")]
    [SerializeField] private Renderer targetBlock;
    [Tooltip("The same material used by this king.")]
    [SerializeField] private Material kingMaterial;

    public Renderer TargetBlock => targetBlock;
    public Material KingMaterial => kingMaterial;

    public void Configure(Renderer block, Material material)
    {
        targetBlock = block;
        kingMaterial = material;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryApply(other);
    }

    public bool TryApply(Collider other)
    {
        // Retain the old script GUID while existing scenes are upgraded.
        // Material changes are no longer part of the king mechanic.
        KingTeleportTrigger portal = GetComponent<KingTeleportTrigger>();
        return portal != null && portal.TryTeleport(other);
    }
}
