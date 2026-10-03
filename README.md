# SignBridge
**Theory and Applications of Virtual Reality (Spring 2026)**

SignBridge is an Augmented Reality (AR) mobile application that bridges the communication gap between deaf/hard-of-hearing individuals and hearing individuals. The app utilizes offline speech recognition (Vosk) to listen to a user's speech and translates the transcribed text into 3D sign language animations rendered in the real-world environment via AR plane tracking.

## Technical Details
* **Unity Version:** 6000.4.3f1 (Unity 6)
* **Frameworks:** AR Foundation, Vosk Offline Speech Recognition
* **Target Platform:** Android (Mobile)

## How to Run the Application

### 1. Running in the Unity Editor
1. Open the project in Unity **6000.4.3f1**.
2. Navigate to `Assets/Scenes/` and open the main scene (e.g., `VoiceSignScene`).
3. Press **Play** in the Unity Editor.
4. *Note:* Because the app uses AR Foundation, some AR features (like plane detection) will use the XR Simulation subsystem or require a physical device for full testing. You can use your PC's microphone to test the Vosk speech recognition.

### 2. Building for Android (Physical Device)
1. Ensure you have the **Android Build Support** module installed for Unity 6000.4.3f1.
2. Go to **File > Build Settings**.
3. Select **Android** and click **Switch Platform**.
4. Connect your Android device via USB (ensure USB Debugging is enabled in Developer Options).
5. Click **Build and Run**.
6. Once the app launches on your phone, grant Camera and Microphone permissions.
7. Point your camera at a flat surface (floor or table) and move it around slightly so AR Foundation can map the environment.
8. Tap the screen when a plane is detected to place the 3D hands.
9. Speak into your microphone; the text will transcribe offline and the hands will perform the corresponding sign language.
