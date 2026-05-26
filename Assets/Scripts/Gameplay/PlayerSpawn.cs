using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;

namespace Platformer.Gameplay
{
    public class PlayerSpawn : Simulation.Event<PlayerSpawn>
    {
        public PlayerController player;
        PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public override void Execute()
        {
            player.collider2d.enabled = true;
            player.controlEnabled = false;
            if (player.audioSource && player.respawnAudio)
                player.audioSource.PlayOneShot(player.respawnAudio);
            player.health.Increment();
            player.Teleport(model.spawnPoint.transform.position);
            player.jumpState = PlayerController.JumpState.Grounded;
            player.animator.SetBool("dead", false);
            Simulation.Schedule<EnablePlayerInput>(2f).player = player;
        }
    }
}