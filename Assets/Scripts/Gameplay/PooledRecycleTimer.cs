using System.Collections;
using UnityEngine;

/// <summary>
/// 池化 one-shot 对象的回收计时器（2.3）。
/// 到时后优先回收到对象池；若未注入 recycler 则回退为 Destroy。
/// </summary>
public sealed class PooledRecycleTimer : MonoBehaviour
{
    private IGameObjectRecycler _recycler;
    private Coroutine _routine;

    public void Arm(IGameObjectRecycler recycler, float seconds)
    {
        _recycler = recycler;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        // 重置常见 VFX 组件，保证“复用”看起来与“新实例化”一致。
        ResetVfxState();

        if (seconds <= 0f)
        {
            return;
        }

        _routine = StartCoroutine(ExpireAfterSeconds(seconds));
    }

    private void OnDisable()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    private IEnumerator ExpireAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _routine = null;

        if (_recycler != null)
        {
            _recycler.Recycle(gameObject);
            yield break;
        }

        Destroy(gameObject);
    }

    private void ResetVfxState()
    {
        // 常见 VFX 组件（ParticleSystem / TrailRenderer）：复用前需清理并重新播放，否则会残留上一轮状态。
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var ps = particles[i];
            if (ps == null)
            {
                continue;
            }

            ps.Clear(true);
            ps.Play(true);
        }

        var trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            var tr = trails[i];
            if (tr == null)
            {
                continue;
            }

            tr.Clear();
        }
    }
}
