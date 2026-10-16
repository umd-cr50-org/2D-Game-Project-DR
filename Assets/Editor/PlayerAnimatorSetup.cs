using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the Player animator: parameters, states and transitions that match PlayerController.
// Run it from the menu: Tools > Setup Player Animator (safe to run more than once).
public static class PlayerAnimatorSetup
{
    private const string ControllerPath = "Assets/Player.controller";

    [MenuItem("Tools/Setup Player Animator")]
    private static void Setup()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("PlayerAnimatorSetup: no AnimatorController found at " + ControllerPath);
            return;
        }

        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
        }

        // Parameters set by PlayerController
        AddBool(controller, "Walking");
        AddBool(controller, "Jumping");
        AddBool(controller, "DoubleJumping");

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = GetOrAddState(sm, "Idle", "Assets/Player_Idle.anim", new Vector3(300, 0));
        AnimatorState walk = GetOrAddState(sm, "Walk", "Assets/Player_Walk.anim", new Vector3(300, 100));
        AnimatorState jump = GetOrAddState(sm, "Jump", "Assets/Player_Jump.anim", new Vector3(550, 0));
        AnimatorState doubleJump = GetOrAddState(sm, "DoubleJump", "Assets/Player_Double_Jump.anim", new Vector3(550, 100));
        sm.defaultState = idle;

        // Rebuild our transitions from scratch so re-running doesn't stack duplicates
        foreach (AnimatorState state in new[] { idle, walk, jump, doubleJump })
        {
            foreach (AnimatorStateTransition t in state.transitions.ToArray())
            {
                state.RemoveTransition(t);
            }
        }
        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
        {
            if (t.destinationState == jump || t.destinationState == doubleJump)
            {
                sm.RemoveAnyStateTransition(t);
            }
        }

        // Ground movement
        Instant(idle.AddTransition(walk)).AddCondition(AnimatorConditionMode.If, 0, "Walking");
        Instant(walk.AddTransition(idle)).AddCondition(AnimatorConditionMode.IfNot, 0, "Walking");

        // In the air: first jump (or falling)
        AnimatorStateTransition toJump = Instant(sm.AddAnyStateTransition(jump));
        toJump.canTransitionToSelf = false;
        toJump.AddCondition(AnimatorConditionMode.If, 0, "Jumping");

        // Pressing jump again in the air plays the double jump
        AnimatorStateTransition toDoubleJump = Instant(sm.AddAnyStateTransition(doubleJump));
        toDoubleJump.canTransitionToSelf = false;
        toDoubleJump.AddCondition(AnimatorConditionMode.If, 0, "DoubleJumping");

        // Landing
        AnimatorStateTransition jumpToIdle = Instant(jump.AddTransition(idle));
        jumpToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Jumping");
        jumpToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "DoubleJumping");
        Instant(doubleJump.AddTransition(idle)).AddCondition(AnimatorConditionMode.IfNot, 0, "DoubleJumping");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // Make sure the player in the open scene actually uses this controller
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            Animator anim = player.GetComponent<Animator>();
            if (anim == null)
            {
                Debug.LogWarning("PlayerAnimatorSetup: the Player has no Animator component.");
            }
            else if (anim.runtimeAnimatorController != controller)
            {
                Undo.RecordObject(anim, "Assign Player Animator");
                anim.runtimeAnimatorController = controller;
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            }
        }

        Debug.Log("PlayerAnimatorSetup: Player animator is set up.");
    }

    private static void AddBool(AnimatorController controller, string name)
    {
        if (!controller.parameters.Any(p => p.name == name))
        {
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        }
    }

    private static AnimatorState GetOrAddState(AnimatorStateMachine sm, string name, string clipPath, Vector3 position)
    {
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (state == null)
        {
            state = sm.AddState(name, position);
        }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            Debug.LogWarning("PlayerAnimatorSetup: missing clip " + clipPath);
        }
        state.motion = clip;
        return state;
    }

    // No exit time and no blending, so the animation switches the moment the bool changes
    private static AnimatorStateTransition Instant(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.duration = 0f;
        return transition;
    }
}
