using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

// Lets you drag and resize an attack's hitbox in the Scene view.
// Select the Player and set Player Attack > Preview Attack to 1-4: that attack's box gets handles
// (drag the edge dots to resize, the center square to move). Changes are saved and can be undone.
[CustomEditor(typeof(PlayerAttack))]
public class PlayerAttackHitboxEditor : Editor
{
    private readonly BoxBoundsHandle boxHandle = new BoxBoundsHandle();

    void OnSceneGUI()
    {
        PlayerAttack attack = (PlayerAttack)target;
        serializedObject.Update();

        SerializedProperty attacks = serializedObject.FindProperty("attacks");
        int preview = serializedObject.FindProperty("previewAttack").intValue;
        if (preview < 1 || preview > attacks.arraySize)
        {
            return;
        }

        SerializedProperty hitbox = attacks.GetArrayElementAtIndex(preview - 1).FindPropertyRelative("hitbox");
        SerializedProperty offsetProp = hitbox.FindPropertyRelative("offset");
        SerializedProperty sizeProp = hitbox.FindPropertyRelative("size");

        // Same mapping as PlayerAttack.GetHitboxWorld: local units, flipped by facing, scaled with the player
        Transform t = attack.transform;
        Vector2 scale = new Vector2(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
        float facing = attack.FacingSign();
        Vector2 offset = offsetProp.vector2Value;
        Vector2 size = sizeProp.vector2Value;
        Vector3 center = t.position + new Vector3(offset.x * facing * scale.x, offset.y * scale.y, 0f);

        Color color = PlayerAttack.HitboxColors[(preview - 1) % PlayerAttack.HitboxColors.Length];
        boxHandle.axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y;
        boxHandle.SetColor(color);
        boxHandle.center = center;
        boxHandle.size = new Vector3(size.x * scale.x, size.y * scale.y, 0f);

        EditorGUI.BeginChangeCheck();
        boxHandle.DrawHandle();
        Vector3 movedCenter = Handles.FreeMoveHandle(boxHandle.center, HandleUtility.GetHandleSize(boxHandle.center) * 0.08f,
            Vector3.zero, Handles.RectangleHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 newCenter = movedCenter != boxHandle.center ? movedCenter : boxHandle.center;
            offsetProp.vector2Value = new Vector2(
                (newCenter.x - t.position.x) / (facing * scale.x),
                (newCenter.y - t.position.y) / scale.y);
            sizeProp.vector2Value = new Vector2(boxHandle.size.x / scale.x, boxHandle.size.y / scale.y);
            serializedObject.ApplyModifiedProperties();
        }

        Handles.Label(center + Vector3.up * (boxHandle.size.y * 0.5f + HandleUtility.GetHandleSize(center) * 0.2f),
            $"Attack {preview} hitbox");
    }
}
