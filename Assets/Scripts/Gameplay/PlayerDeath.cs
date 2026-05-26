using Platformer.Core;
using Platformer.Mechanics;

namespace Platformer.Gameplay
{
    public class PlayerDeath : Simulation.Event<PlayerDeath>
    {
        public PlayerController player;

        public override void Execute()
        {
            if (player.health.IsAlive)
            {
                player.health.Die();
                player.controlEnabled = false;

                if (player.audioSource && player.ouchAudio)
                    player.audioSource.PlayOneShot(player.ouchAudio);
                player.animator.SetTrigger("hurt");
                player.animator.SetBool("dead", true);
                Simulation.Schedule<PlayerSpawn>(2).player = player;
            }
        }
    }
}