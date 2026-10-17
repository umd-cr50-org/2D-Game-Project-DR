using UnityEngine;

// The player's input: arrow keys / A-D to move, Jump button to jump, attackKey to attack, magicKey for magic.
// Dashing is a double tap of left/right, which PlayerController detects from Horizontal.
// PlayerController adds this automatically if the object has no other CharacterInput.
public class KeyboardCharacterInput : CharacterInput
{
    [SerializeField] private KeyCode attackKey = KeyCode.Z;
    [SerializeField] private KeyCode magicKey = KeyCode.X;

    public override float Horizontal => Input.GetAxisRaw("Horizontal");
    public override bool JumpPressed => Input.GetButtonDown("Jump");
    public override bool JumpReleased => Input.GetButtonUp("Jump");
    public override bool AttackPressed => Input.GetKeyDown(attackKey);
    public override int DashDirection => 0;
    public override bool MagicPressed => Input.GetKeyDown(magicKey);
}
