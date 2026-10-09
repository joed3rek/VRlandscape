# VR Landscape: Park Walkthrough

A Unity app for Android that lets clients and the design team experience a landscape (park) design in two ways:

| Mode | What it shows | Source |
|---|---|---|
| **Walk in 3D** | Free walking through the real 3D model | Rhino (glTF/FBX export) |
| **360° Tour** | Photoreal 360° views at chosen spots, jump between them with hotspots | Lumion 360° panorama renders |

One APK runs on both:

- **Meta Quest**: full VR. Left stick walks, click the stick to run. Right stick snap-turns. Trigger selects. Menu/B/Y goes back.
- **Android phone, no headset**: move the phone to look around (gyroscope). Drag to turn, use the on-screen joystick to walk, tap markers to select, press Back to return to the menu.

A built-in **Demo Park** (procedural fountain, pavilion, pond and trees) works out of the box, so you can test before adding your own content.

---

## 1. One-time setup

1. In **Unity Hub**, open **Installs → Install Editor → Unity 6 LTS (6000.0.x)** and tick these modules:
   - **Android Build Support**, including **OpenJDK** and **Android SDK & NDK Tools**
2. In Unity Hub, open **Projects → Add → Add project from disk** and pick this folder (`VRlandscape`). Open it with Unity 6.
   - The first import downloads the packages (glTFast, OpenXR, Input System) and takes a few minutes.
   - If Unity asks to enable the new Input System backend, click **Yes**. The editor will restart.
3. Open **Edit → Project Settings → XR Plug-in Management** once, and click both the **Android** tab and the **Desktop** tab. This creates the XR settings files.
4. Run the menu **VR Landscape → 1. Configure Project (Android, Quest + Phone)**, then click **Switch** when it asks about Android.
   This menu:
   - creates the main scene
   - sets the package name, IL2CPP/ARM64, landscape orientation and linear colour
   - enables OpenXR with the Meta Quest feature and Touch controllers
   - turns off "Initialize XR on Startup", because the app starts VR only on headsets
5. Press **Play** in the editor. The app runs in phone mode: **WASD** walks, **Shift** runs, **drag the mouse** to look, **Q/E** turns.
   - With a Quest on **Quest Link**, tick **VR Landscape → Use Headset in Play Mode** to test VR in the editor.

## 2. Building and installing

- Run **VR Landscape → 2. Build APK**. This creates `Builds/VRLandscape.apk`. You can also use **File → Build Profiles**.
- **Phone:** enable Developer Options and USB debugging on the phone, then run `adb install -r Builds/VRLandscape.apk`. You can also copy the APK to the phone and open it there.
- **Quest:** turn on Developer Mode for the headset in the Meta Horizon phone app. Connect over USB, then install with `adb install -r Builds/VRLandscape.apk` or with **Meta Quest Developer Hub**. On the headset the app is under **Library → Unknown Sources**.

`adb` is in the Android SDK's `platform-tools` folder.

---

## 3. Adding a park

Each park is a folder that contains a `park.json` file, plus the model and/or panorama images:

```
RiversidePark/
  park.json
  model.glb
  panoramas/
    entrance.jpg
    plaza.jpg
```

You can put the folder in either of two places:

- **On the device (no rebuild needed).** Connect the phone or Quest over USB and copy the folder into
  `Android/data/com.vrlandscape.parkwalkthrough/files/Parks/`. Then press **Reload parks** in the app. The folder is created the first time the app runs. The menu footer shows the exact path.
- **Inside the APK.** Put the folder in `Assets/StreamingAssets/Parks/` and add the folder name to `Assets/StreamingAssets/Parks/index.json`:
  ```json
  { "parks": ["RiversidePark"] }
  ```

See [Assets/StreamingAssets/Parks/_Template/park.json](Assets/StreamingAssets/Parks/_Template/park.json) for a complete example.

### park.json reference

| Field | Meaning |
|---|---|
| `name`, `description` | Shown in the park menu |
| `model` | `.glb` (recommended) or `.gltf` file, relative to the folder. Turns on **Walk in 3D**. |
| `resourcePrefab` | Alternative to `model`: a prefab path under a `Resources` folder (for FBX files imported in the editor), for example `"Parks/Riverside"` |
| `scale` | Multiplier, if the model's units aren't metres |
| `collisions` | `true` stops people walking through walls and lets them walk on terrain and steps |
| `spawn.position` / `spawn.yaw` | Where the walk starts (feet position in metres), and which way the viewer faces |
| `panoramas[]` | Turns on **360° Tour** |
| `panoramas[].image` | Equirectangular 2:1 JPG/PNG |
| `panoramas[].position` | Optional. Where the panorama was taken, as `[x, eyeHeight, z]`. Shows a blue **360°** marker in the 3D walk. |
| `panoramas[].yawOffset` | Rotates the image, if the image centre doesn't face the model's +Z ("north") |
| `hotspots[]` | `target` (panorama id), `label`, and either `yaw`/`pitch` in degrees, or `"auto": true` to aim at the target's `position` |
| `startPanorama` | The id of the panorama the tour opens with |

**Finding yaw, pitch and positions:** tap **Info** in the phone HUD. It shows the current look yaw and pitch, and your position, which you can copy into `park.json`.

Coordinates use Unity space: Y is up and units are metres. Yaw 0 looks along +Z, and positive yaw turns clockwise when seen from above.

---

## 4. Exporting from Rhino (3D walkthrough)

1. Set the document units to **metres** (or use `scale` in park.json).
2. Tidy the model for mobile:
   - delete hidden layers, construction lines and 2D linework
   - mesh with sensible density
   - **Quest budget:** about **500k to 1M triangles** for the whole scene, and textures **2048 px or smaller**
3. Export:
   - **Rhino 8 and later:** **File → Export Selected → glTF Binary (.glb)**. Rhino converts Z-up to Y-up automatically.
   - **Other versions:** export **FBX**, drag it into `Assets/VRLandscape/Resources/Parks/` in Unity, and make a prefab. Then point `resourcePrefab` at it (for example `"Parks/Riverside"`), so the park ships inside the APK.
4. Include terrain and ground surfaces. These are what people walk on. If nothing is below the spawn point, the viewer falls and respawns.

Tips: give materials simple colours or textures, because complex Rhino render materials don't carry over. Planting from Lumion doesn't export, so use the 360° tour to show the fully rendered planting.

## 5. Exporting from Lumion (360° tour)

1. In Lumion, place a camera at each viewpoint at **eye height (about 1.6 m)**.
2. Use **Panorama** mode, choose the **mono equirectangular / 360°** output, and render.
   - **Phones:** use **4096 × 2048**
   - **Quest:** use **6144 × 3072** or smaller, because 8K images use a lot of memory
3. Save the images as JPG in the park's `panoramas/` folder, and add one entry per viewpoint to `park.json`.
4. Link the viewpoints with hotspots. Set a panorama's `position` to the same spot in the Rhino coordinates, and a 360° marker also appears in the 3D walk at that spot.

---

## 6. On-site mode (AR render window)

Visitors walk the real site with the phone. Each Lumion 360° render is tied to the GPS spot where it was taken. When a visitor arrives (within about 8–10 m), that render opens over the live camera, lined up with the real directions. A **NOW ↔ PROPOSED** slider fades between today's site and the design.

**Setting the spots on site (once):**
1. Open the park and tap **ON SITE**.
2. Stand where the Lumion camera was, **face the direction the render looks toward**, and tap **SET HERE** on that spot.
3. Open the spot. If the render is slightly rotated, tap **ALIGN**, drag sideways until it lines up, then tap **SAVE**.

Spots are saved on the phone. To share them, put them in `park.json` instead. Each panorama takes a `geo` entry:

```json
{ "id": "entrance", "image": "panoramas/entrance.jpg",
  "geo": { "lat": 12.97160, "lon": 77.59460, "heading": 35 } }
```

- `lat` / `lon`: long-press the spot in Google Maps to copy them.
- `heading`: the compass direction the centre of the render faces (0 = north, 90 = east).

**Accuracy:** phone GPS is typically ±3–8 m outdoors and worse beside tall buildings. The arrival radius grows with the GPS error. The compass is usually within about 10°; use **ALIGN** to fix the rest.

## 7. View Sync (presenter mode)

One person presents and everyone else sees the same view on their own phone (or Quest). No router, hotspot or mobile data is needed.

- **Presenter:** tap **SYNC** (in the park menu, or at the top of the Walk in 3D and 360° screens) → **PRESENT → START**. The phone creates and advertises a private Wi-Fi Direct room. A **LIVE • N VIEWERS** badge stays on screen.
- **Viewers:** tap **SYNC**. Nearby presentations appear by themselves. With exactly one nearby, the phone joins automatically after a 3-second countdown (with **Cancel**); otherwise tap **JOIN** on the one you want. **Use a room code** is a backup if discovery doesn't find it.
- The SYNC screen checks the setup itself. If Wi-Fi is off it shows **TURN ON WI-FI**, and if permission is missing it shows **ALLOW**. Wi-Fi only needs to be switched on, not connected to anything. Keep the hotspot **off**.
- Viewers follow the presenter's park, mode (Walk in 3D or 360° tour), viewpoint and camera. **Touching the screen** lets them roam on their own. **← PRESENTER VIEW** snaps them back. On a Quest, the **A** button does the same.
- AR and On-site are personal to each phone, so they aren't mirrored.
- Every viewer needs the same parks on their phone (same names). The presenter streams only where they are looking, not the model itself.
- Range is about 30–50 m outdoors. Rooms need Android 10 or newer, and phones ask once for the **Nearby devices** permission.

## Project layout

```
Assets/VRLandscape/
  Scripts/Core/         App flow (App.cs), park loading, XR start-up, file I/O
  Scripts/Rig/          Player body/camera, phone gyro+touch controls, Quest sticks + laser
  Scripts/Walkthrough/  glTF/prefab model loading, demo park
  Scripts/Panorama/     360 viewer and hotspots
  Scripts/UI/           Menu, phone HUD, joystick (built in code, no prefabs)
  Resources/VRLandscape Panorama + unlit shaders
  Editor/               "VR Landscape" menu (project setup, APK build)
Assets/StreamingAssets/Parks/   Parks bundled into the APK
```

The app boots itself in any scene through `App.Boot()`, so nothing needs wiring in the editor.

## Troubleshooting

- **The model is pink or invisible on the device:** glTFast shaders are missing from the build. Run the Configure menu again, or add glTFast's `glTFShaderVariantsBuiltIn` collection under **Project Settings → Graphics → Preloaded Shaders**.
- **The phone starts in VR or crashes on launch:** make sure **Initialize XR on Startup** is off under XR Plug-in Management → Android.
- **The gyro look is rotated or upside down on a particular phone:** turn the gyro off in the HUD and use drag-to-look, and report the phone model. The conversion is in `PhoneControls.DeviceToUnity`.
- **People fall through the ground:** the ground mesh may be missing, or `collisions` is off. Check that `spawn` is above the terrain.
