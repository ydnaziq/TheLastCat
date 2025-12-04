using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using DG.Tweening;

public class PlayerWeaponsManager : MonoBehaviour
{
    private InputAction equipAction;

    public float equipRadius = 1.5f;

    public List<PlayerWeaponController> equippedSpears = new List<PlayerWeaponController>();

    [Header("Slash Settings")]
    private InputAction slashAction;
    private int currentSpearIndex = 0;
    public bool globalSlashOnCooldown = false;
    [SerializeField] private float globalSlashCooldown = 1f;

    void Start()
    {
        slashAction = InputSystem.actions.FindAction("Attack");
        equipAction = InputSystem.actions.FindAction("Interact");
    }

    void Update()
    {
        if (equipAction.WasPressedThisFrame())
        {
            TryEquip();
        }
        if (slashAction.WasPressedThisFrame())
        {
            TrySlash();
        }
    }

    public void TryEquip()
    {
        Physics2D.OverlapCircleAll(transform.position, equipRadius);
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, equipRadius);

        if (hits.Length > 0)
        {
            PlayerWeaponController closest = null;
            PlayerWeaponController closestUnequipped = null;
            float closestDist = Mathf.Infinity;
            float closestUnequippedDist = Mathf.Infinity;

            foreach (var h in hits)
            {
                if (!h.CompareTag("Equippable")) continue;
                var weapon = h.GetComponent<PlayerWeaponController>();
                if (!weapon) continue;

                float d = Vector2.Distance(transform.position, weapon.transform.position);

                if (d < closestDist)
                {
                    closest = weapon;
                    closestDist = d;
                }

                if (!weapon.IsEquipped && d < closestUnequippedDist)
                {
                    closestUnequipped = weapon;
                    closestUnequippedDist = d;
                }
            }

            if (closestUnequipped != null)
            {
                RegisterSpear(closestUnequipped);
                closestUnequipped.IsEquipped = true;

                PlayEquipAnimation(closestUnequipped, transform);
                return;
            }

            if (closest != null)
            {
                UnregisterSpear(closest);
                closest.IsEquipped = false;

                PlayUnequipAnimation(closest);
                return;
            }

            return;
        }
    }

    void TrySlash()
    {
        if (equippedSpears.Count == 0) return;
        if (globalSlashOnCooldown) return;
        var spear = equippedSpears[currentSpearIndex];
        if (spear.onCooldown) return;
        StartCoroutine(spear.ProjectileRoutine());

        currentSpearIndex++;

        if (currentSpearIndex >= equippedSpears.Count)
        {
            currentSpearIndex = 0;
            StartCoroutine(GlobalSlashCooldownRoutine());
        }
    }

    public void PlayEquipAnimation(PlayerWeaponController spear, Transform player)
    {
        Transform t = spear.transform;
        Rigidbody2D rb = spear.GetComponent<Rigidbody2D>();

        t.DOKill(true); // stop any leftover tweens

        // Reset parent + physics
        t.parent = null;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        Vector3 origScale = spear.originalScale;

        // Short pop upward
        Vector3 popOffset = new Vector3(0, 0.35f, 0);

        t.DOMove(t.position + popOffset, 0.12f)
            .SetEase(Ease.OutQuad);

        // Slide into the player's hand
        t.DOMove(player.position, 0.18f)
            .SetEase(Ease.InOutQuad)
            .SetDelay(0.05f);

        // Smooth rotation into place
        t.DORotate(new Vector3(0, 0, player.eulerAngles.z), 0.20f)
            .SetEase(Ease.OutCubic)
            .SetDelay(0.05f);

        // Small scale pop for feedback
        t.DOScale(origScale * 1.1f, 0.1f)
            .From(origScale * 0.85f)
            .SetDelay(0.05f)
            .SetEase(Ease.OutBack);

        // Ensure final scale is correct
        t.DOScale(origScale, 0.1f)
            .SetEase(Ease.OutCubic)
            .SetDelay(0.15f);
    }



    public void PlayUnequipAnimation(PlayerWeaponController spear)
    {
        Transform t = spear.transform;
        Rigidbody2D rb = spear.GetComponent<Rigidbody2D>();

        // Restore original scale
        t.DOScale(spear.originalScale, 0.2f).SetEase(Ease.OutCubic);

        // Apply spin (visual)
        t.DORotate(new Vector3(0, 0, -720f), 0.5f, RotateMode.FastBeyond360)
         .SetEase(Ease.OutQuad);

        // Physics knock-away
        if (rb != null)
        {
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            float force = Random.Range(5f, 10f);

            rb.AddForce(randomDir * force, ForceMode2D.Impulse);
            rb.AddTorque(Random.Range(-5f, 5f), ForceMode2D.Impulse);
        }
    }


    public void RegisterSpear(PlayerWeaponController spear)
    {
        if (!equippedSpears.Contains(spear))
            equippedSpears.Add(spear);
    }

    public void UnregisterSpear(PlayerWeaponController spear)
    {
        equippedSpears.Remove(spear);
    }

    private IEnumerator GlobalSlashCooldownRoutine()
    {
        globalSlashOnCooldown = true;
        yield return new WaitForSeconds(globalSlashCooldown);
        globalSlashOnCooldown = false;
    }

}
