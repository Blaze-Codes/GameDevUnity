using UnityEngine;
using System.Collections;
using Platformer.Mechanics;

public class VirtualPlayerInput : MonoBehaviour
{
    public PlayerController targetPlayer;

    private Coroutine moveCoroutine;

    void Start()
    {
        targetPlayer.useExternalInput = true;
    }

    void OnDisable()
    {
        if (targetPlayer != null)
        {
            targetPlayer.useExternalInput = false;
            targetPlayer.externalMoveX = 0;
        }
    }

    public void SetMove(float direction, float duration = 0.5f)
    {
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(SimulateMove(direction, duration));
    }

    private IEnumerator SimulateMove(float direction, float duration)
    {
        targetPlayer.externalMoveX = Mathf.Clamp(direction, -1f, 1f);
        yield return new WaitForSeconds(duration);
        targetPlayer.externalMoveX = 0;
        moveCoroutine = null;
    }

    public void Jump()
    {
        targetPlayer.externalJump = true;
    }
}
