using UnityEngine;

// Where a character's "button presses" come from. PlayerController and PlayerAttack read only this,
// so the same movement and attacks can be driven by the keyboard (the player) or by an AI (the boss).
// Runs before other scripts each frame so the controller always sees this frame's input.
[DefaultExecutionOrder(-100)]
public abstract class CharacterInput : MonoBehaviour
{
    public abstract float Horizontal { get; } // -1 .. 1
    public abstract bool JumpPressed { get; } // true only on the frame jump is pressed
    public abstract bool JumpReleased { get; } // true only on the frame jump is let go (cuts the jump short)
    public abstract bool AttackPressed { get; } // true only on the frame attack is pressed
    public abstract int DashDirection { get; } // -1 / 1 on the frame a dash is requested directly, otherwise 0
    public virtual bool MagicPressed => false; // true only on the frame magic is pressed (used by MagicCaster)
}
