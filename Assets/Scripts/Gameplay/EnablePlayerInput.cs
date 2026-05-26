using Platformer.Core;
using Platformer.Mechanics;

namespace Platformer.Gameplay
{
    public class EnablePlayerInput : Simulation.Event<EnablePlayerInput>
    {
        public PlayerController player;

        public override void Execute()
        {
            player.controlEnabled = true;
        }
    }
}