# Voice-to-Sign MVP Setup

1. Open `Assets/Scenes/VoiceSignDemo.unity`.
2. Press Play. `VoiceSignSceneBootstrap` auto-creates the UI/system if missing.
3. Add your Gemini API key in the `SentenceScoringService` component (`apiKey`).
4. For Android with KKSpeech plugin, add scripting define `KKSPEECH_PRESENT`.
5. Assign your avatar Animator to `SignAnimationController.targetAnimator`.

Notes:
- Without API key, sentence score uses local fallback heuristic.
- In Editor, voice defaults to deterministic demo phrases for reliability.
