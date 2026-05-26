using UnityEngine;
using Platformer.Mechanics;

public class LLMPlayerTool : MonoBehaviour
{
    [Header("Target Player")]
    public PlayerController player;

    private float moveInput = 0f;
    private bool jumpRequested = false;

    void Update()
    {
        if (player == null) return;

        ApplyMovement();
        ApplyJump();
    }

    // =========================
    // TOOL: MOVE
    // =========================
    public void Tool_Move(float direction)
    {
        moveInput = Mathf.Clamp(direction, -1f, 1f);
    }

    // =========================
    // TOOL: JUMP
    // =========================
    public void Tool_Jump()
    {
        jumpRequested = true;
    }

    private void ApplyMovement()
    {
        player.SetExternalMove(moveInput);
    }

    private void ApplyJump()
    {
        if (!jumpRequested) return;

        player.SetExternalJump();
        jumpRequested = false;
    }
}