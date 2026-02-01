using UnityEngine;

public class PlayOneShotBehaviour : StateMachineBehaviour
{
    public AudioClip soundToPlay;
    public float volume = 1f;
    public bool playOnEnter = true;
    public bool playOnExit = false;
    public bool playAfterDelay = false;

    [Tooltip("音效延迟播放（秒）")]
    public float playDelay = 0.25f;

    private float _timeSinceEntered;
    private bool _hasDelayedSoundPlayed;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (playOnEnter && soundToPlay != null)
        {
            AudioSource.PlayClipAtPoint(soundToPlay, animator.transform.position, volume);
        }

        _timeSinceEntered = 0f;
        _hasDelayedSoundPlayed = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (!playAfterDelay || _hasDelayedSoundPlayed)
        {
            return;
        }

        _timeSinceEntered += Time.deltaTime;
        if (_timeSinceEntered > playDelay && soundToPlay != null)
        {
            AudioSource.PlayClipAtPoint(soundToPlay, animator.transform.position, volume);
            _hasDelayedSoundPlayed = true;
        }
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (playOnExit && soundToPlay != null)
        {
            AudioSource.PlayClipAtPoint(soundToPlay, animator.transform.position, volume);
        }
    }
}
