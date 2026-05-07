using UnityEngine;

namespace BattleChess
{
    public sealed class AssetTracker : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log(gameObject.name);
        }
    }
}
