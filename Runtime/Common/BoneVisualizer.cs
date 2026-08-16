using UnityEngine;

namespace EAStudio.Core
{
    [AddComponentMenu("EAStudio/Common/Bone Visualizer")]
    public class BoneVisualizer : MonoBehaviour
    {
        public Color boneColor = Color.cyan;
        public float jointSize = 0.05f;

        private void OnDrawGizmos()
        {
            // 从当前物体开始，递归绘制所有子骨骼
            DrawBones(transform);
        }

        private void DrawBones(Transform current)
        {
            foreach (Transform child in current)
            {
                // 绘制当前节点到子节点的连线（骨骼）
                Gizmos.color = boneColor;
                Gizmos.DrawLine(current.position, child.position);

                // 绘制关节球
                Gizmos.DrawSphere(child.position, jointSize);

                // 递归子节点
                DrawBones(child);
            }
        }
    }
}
