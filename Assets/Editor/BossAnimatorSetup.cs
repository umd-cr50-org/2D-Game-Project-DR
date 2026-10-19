using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the Boss animator from scratch and wires the boss in the open scene to it.
// Run it from the menu: Tools > Setup Boss Animator (safe to run more than once - it rebuilds every time).
//   - Boss.controller: the bool parameters the boss's scripts send, one state per Boss_ clip (Boss_Idle default),
//     Any State -> Boss_Jumping whenever InAir is on and no attack is (back to Boss_Idle on landing),
//     Boss_Idle -> Boss_AttackN when AttackN is on and back when it turns off.
//     Boss_Idle <-> Boss_Magic while Casting is on.
//     (Attacks and magic are also started directly from code, so they cut into Boss_Jumping in the air.)
//   - Gives the boss a Magic Caster that plays Boss_Magic.
//   - The boss object (the one with BossAI): uses Boss.controller, its Player Attack uses the Boss_Attack animations,
//     its Rigidbody2D matches the player's (same gravity and physics material, heavy so it can't be shoved),
//     and its Player Controller gets the player's movement settings (speed, jump, dash...).
public static class BossAnimatorSetup
{
    private const string ControllerPath = "Assets/Boss.controller";
    private const string AttackPrefix = "Boss_Attack";

    [MenuItem("Tools/Setup Boss Animator")]
    private static void Setup()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }
        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
        }

        foreach (string name in new[] { "Walking", "Jumping", "DoubleJumping", "Dashing", "FreeFall", "InAir", "Casting", "Attack1", "Attack2", "Attack3", "Attack4" })
        {
            if (!controller.parameters.Any(p => p.name == name))
            {
                controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            }
        }

        // Start from an empty graph: removes duplicate states and any half-made transitions
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
        {
            sm.RemoveAnyStateTransition(t);
        }
        foreach (ChildAnimatorState child in sm.states.ToArray())
        {
            sm.RemoveState(child.state);
        }

        AnimatorState idle = AddState(sm, "Boss_Idle", new Vector3(300, 0), loop: true);
        sm.defaultState = idle;

        // In the air, Boss_Jumping is all you see (jumps, double jumps, falls, dashes, free fall) - from any state,
        // except while an attack is playing. Attacks cut into it, and when one ends in the air it comes straight
        // back here (Any State transitions are checked before the attack's own way back to idle).
        AnimatorState jumping = AddState(sm, "Boss_Jumping", new Vector3(0, 0), loop: false);
        AnimatorStateTransition toAir = Instant(sm.AddAnyStateTransition(jumping));
        toAir.canTransitionToSelf = false;
        toAir.AddCondition(AnimatorConditionMode.If, 0, "InAir");
        for (int n = 1; n <= 4; n++)
        {
            toAir.AddCondition(AnimatorConditionMode.IfNot, 0, "Attack" + n);
        }
        toAir.AddCondition(AnimatorConditionMode.IfNot, 0, "Casting");
        Instant(jumping.AddTransition(idle)).AddCondition(AnimatorConditionMode.IfNot, 0, "InAir");
        for (int n = 1; n <= 4; n++)
        {
            string stateName = n == 1 ? AttackPrefix : AttackPrefix + n;
            AnimatorState attack = AddState(sm, stateName, new Vector3(600, -150 + (n - 1) * 100), loop: false);
            Instant(idle.AddTransition(attack)).AddCondition(AnimatorConditionMode.If, 0, "Attack" + n);
            Instant(attack.AddTransition(idle)).AddCondition(AnimatorConditionMode.IfNot, 0, "Attack" + n);
        }
        // Walking, running and dashing: played straight from code (BossAI's Run Animation), so no links needed
        AddState(sm, "Boss_Running", new Vector3(0, 150), loop: true);

        // Magic: MagicCaster plays Boss_Magic directly and holds "Casting" on until the spell is done
        AnimatorState magic = AddState(sm, "Boss_Magic", new Vector3(300, 200), loop: false);
        Instant(idle.AddTransition(magic)).AddCondition(AnimatorConditionMode.If, 0, "Casting");
        Instant(magic.AddTransition(idle)).AddCondition(AnimatorConditionMode.IfNot, 0, "Casting");

        // Not driven by code yet (defeat), so it's placed but not connected
        AddState(sm, "Boss_Lose", new Vector3(300, 300), loop: false);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        SetUpBossObject(controller);
        Debug.Log("BossAnimatorSetup: Boss animator is set up. Save the scene (Ctrl+S) to keep the boss object changes.");
    }

    private static void SetUpBossObject(AnimatorController controller)
    {
        BossAI boss = Object.FindAnyObjectByType<BossAI>();
        if (boss == null)
        {
            Debug.LogWarning("BossAnimatorSetup: no object with BossAI in the open scene, so only the controller was built.");
            return;
        }
        GameObject go = boss.gameObject;

        Animator anim = go.GetComponent<Animator>();
        if (anim == null)
        {
            anim = Undo.AddComponent<Animator>(go);
        }
        Undo.RecordObject(anim, "Assign Boss Animator");
        anim.runtimeAnimatorController = controller;

        // Magic: the boss casts with Boss_Magic (BossAI aims it at the player)
        MagicCaster caster = go.GetComponent<MagicCaster>();
        if (caster == null)
        {
            caster = Undo.AddComponent<MagicCaster>(go);
        }
        SerializedObject casterSo = new SerializedObject(caster);
        casterSo.FindProperty("castStateName").stringValue = "Boss_Magic";
        casterSo.ApplyModifiedProperties();

        // Boss attacks play Boss_Attack, Boss_Attack2, ... instead of the player's animations
        PlayerAttack attack = go.GetComponent<PlayerAttack>();
        if (attack != null)
        {
            SerializedObject so = new SerializedObject(attack);
            so.FindProperty("animationPrefix").stringValue = AttackPrefix;
            SerializedProperty attacks = so.FindProperty("attacks");
            for (int i = 0; i < attacks.arraySize && i < 4; i++)
            {
                string clipPath = $"Assets/{(i == 0 ? AttackPrefix : AttackPrefix + (i + 1))}.anim";
                attacks.GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            }
            so.ApplyModifiedProperties();
        }

        // Same physics as the player, but heavy enough that the player can't push it around
        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        PlayerController player = Object.FindObjectsByType<PlayerController>()
            .FirstOrDefault(p => p.GetComponent<BossAI>() == null);
        if (rb != null && player != null)
        {
            Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
            Undo.RecordObject(rb, "Match Boss Physics");
            rb.gravityScale = playerRb.gravityScale;
            rb.sharedMaterial = playerRb.sharedMaterial;
            rb.mass = 50f;
            rb.freezeRotation = true;
        }

        // Same movement as the player (walk speed, jump, dash, slopes...), except which way its sprite is drawn
        PlayerController bossBody = go.GetComponent<PlayerController>();
        if (bossBody != null && player != null)
        {
            SerializedObject from = new SerializedObject(player);
            SerializedObject to = new SerializedObject(bossBody);
            SerializedProperty prop = from.GetIterator();
            prop.NextVisible(true); // skip m_Script
            while (prop.NextVisible(false))
            {
                if (prop.name != "spriteFacesRight")
                {
                    to.CopyFromSerializedProperty(prop);
                }
            }
            to.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    private static AnimatorState AddState(AnimatorStateMachine sm, string clipName, Vector3 position, bool loop)
    {
        AnimatorState state = sm.AddState(clipName, position);
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/{clipName}.anim");
        if (clip == null)
        {
            Debug.LogWarning("BossAnimatorSetup: missing clip Assets/" + clipName + ".anim");
            return state;
        }
        state.motion = clip;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime != loop)
        {
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
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
