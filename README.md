README — Full Setup Guide (macOS + Windows)
==========================================

This README covers EVERYTHING from zero to working Unity project:
1) Install package manager (Homebrew on macOS / Winget or Chocolatey on Windows)
2) Install Git
3) Install Git LFS
4) Clone project from GitHub and download LFS assets
5) Open in Unity Hub (same Unity version for everyone)
6) Unity settings for version control (Visible Meta Files + Force Text)
7) Import 3D models (FBX/OBJ/BLEND) correctly + commit with LFS
8) Team Git workflow (branches + PRs) + Unity merge-conflict tips

------------------------------------------------------------
0) Team Rules (READ FIRST)
------------------------------------------------------------
- Everyone MUST use the same Unity Editor version.
- Always commit .meta files.
- Never commit Unity cache/build folders: Library/, Temp/, Obj/, Logs/, Build/Builds/.
- Install Git LFS BEFORE adding big assets (textures/audio/models).
- Avoid multiple people editing the same Scene (*.unity) at the same time.
- Prefer prefabs over editing a single large scene together.

------------------------------------------------------------
1) Unity Version (Fill This In)
------------------------------------------------------------
Unity Editor Version: 6000.3.5f1
Render Pipeline: <URP / Built-in>
Target Platform: <PC / WebGL / Mobile>

Tip:
- Put the exact Unity version above into your main README so everyone installs the same one.

------------------------------------------------------------
2) Install Tools — macOS
------------------------------------------------------------

2.1 Install Homebrew (macOS)
----------------------------
1) Open Terminal (Cmd + Space → type "Terminal" → Enter)
2) Run:
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

3) IMPORTANT: Add Homebrew to PATH (common on Apple Silicon M1/M2/M3)
   If the installer prints commands like these, run them exactly:

   echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> ~/.zprofile
   eval "$(/opt/homebrew/bin/brew shellenv)"

4) Verify:
   brew --version

If you see a version number, Homebrew is installed.

2.2 Install Git (macOS)
-----------------------
brew install git
git --version

2.3 Configure Git identity (do once per computer)
-------------------------------------------------
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
git config --global --list

2.4 Install Git LFS (macOS) — REQUIRED
--------------------------------------
brew install git-lfs
git lfs install
git lfs version

If you see:
"git: 'lfs' is not a git command"
Git LFS is not installed or not on PATH.

------------------------------------------------------------
3) Install Tools — Windows
------------------------------------------------------------

Choose ONE method:
A) winget (recommended, built into modern Windows)
B) Chocolatey
C) Manual installers

3A) Windows using winget (Recommended)
--------------------------------------
1) Open PowerShell (Windows key → type "PowerShell" → Enter)
2) Install Git:
   winget install --id Git.Git -e

3) Install Git LFS (if not included with Git on your machine):
   winget install --id GitHub.GitLFS -e

4) Close PowerShell, reopen it, then verify:
   git --version
   git lfs version

5) Enable LFS:
   git lfs install

3B) Windows using Chocolatey
----------------------------
1) Install Chocolatey (run PowerShell as Administrator):
   Set-ExecutionPolicy Bypass -Scope Process -Force; `
   [System.Net.ServicePointManager]::SecurityProtocol = `
   [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; `
   iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

2) Install Git and Git LFS:
   choco install git git-lfs -y

3) Close and reopen PowerShell, then:
   git --version
   git lfs install
   git lfs version

3C) Windows manual installers (Fallback)
----------------------------------------
1) Install Git for Windows
2) Install Git LFS
3) Reopen PowerShell/Git Bash
4) Verify:
   git --version
   git lfs install
   git lfs version

3.1 Configure Git identity (Windows, do once)
---------------------------------------------
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
git config --global --list

------------------------------------------------------------
4) Clone the Repository (macOS + Windows)
------------------------------------------------------------
Open Terminal (macOS) or PowerShell/Git Bash (Windows), then:

1) Clone:
   git clone <REPOSITORY_URL>
   cd <REPOSITORY_FOLDER_NAME>

2) IMPORTANT: download LFS assets:
   git lfs pull

3) Check:
   git status

------------------------------------------------------------
5) Open the Project in Unity Hub
------------------------------------------------------------
1) Open Unity Hub
2) Click "Open" / "Add"
3) Select the repository folder (must contain: Assets/, Packages/, ProjectSettings/)
4) If prompted, install EXACT Unity version from section 1
5) Open the project and wait for the first import

First-time checks:
- Console shows no red errors
- Open main scene (example): Assets/Scenes/MainScene.unity
- Press Play once to confirm it runs

------------------------------------------------------------
6) Unity Settings for Git (Must Do)
------------------------------------------------------------
In Unity:
Edit → Project Settings → Editor

Set:
- Version Control Mode: Visible Meta Files
- Asset Serialization Mode: Force Text

Why:
- .meta files preserve asset GUID references
- Force Text improves diffs/merges for many Unity YAML files

After setting, Unity may change ProjectSettings files.
Commit (recommended):
git add ProjectSettings
git commit -m "Set Unity editor settings for version control"
git push

------------------------------------------------------------
7) .gitignore (Unity)
------------------------------------------------------------
Make sure the repo root has a Unity .gitignore.

Must ignore:
- Library/
- Temp/
- Obj/
- Logs/
- Build/ and/or Builds/
- UserSettings/ (commonly ignored)
- IDE folders: .vs/ .idea/ .vscode/
- OS files: .DS_Store

Quick check:
git status
You should NOT see Library/ or Temp/ listed.

If those folders were accidentally tracked before:
git rm -r --cached Library Temp Obj Logs
git commit -m "Remove Unity generated folders from tracking"
git push

------------------------------------------------------------
8) Git LFS Tracking for Art Assets (Do once per repo)
------------------------------------------------------------
Run in repo root (recommended patterns):

git lfs track "*.psd" "*.png" "*.jpg" "*.jpeg" "*.tga"
git lfs track "*.wav" "*.mp3" "*.ogg"
git lfs track "*.fbx" "*.obj" "*.blend" "*.glb" "*.gltf"
git lfs track "*.mp4"

Then commit:
git add .gitattributes
git commit -m "Configure Git LFS tracking for art assets"
git push

Verify:
git lfs track
(After assets exist)
git lfs ls-files

IMPORTANT:
- Install Git LFS BEFORE adding large files.
- If you add big files without LFS, the repo history gets messy.

------------------------------------------------------------
9) Importing 3D Models into Unity (FBX/OBJ/BLEND)
------------------------------------------------------------

9.1 Recommended folder structure
-------------------------------
Assets/Art/Models/        (FBX/OBJ/BLEND)
Assets/Art/Textures/      (PNG/JPG/TGA/PSD)
Assets/Art/Materials/     (.mat)
Assets/Prefabs/           (prefabs created from models)
Assets/Scenes/            (scene files)

9.2 Import method (recommended)
-------------------------------
Option A (best): Drag & drop into Project window
1) In Unity Project panel, go to Assets/Art/Models/
2) Drag model file (.fbx/.obj/.blend) into that folder
3) Unity will import automatically

Option B: Copy file into the folder in Finder/Explorer
1) Copy model file into:
   <ProjectRoot>/Assets/Art/Models/
2) Unity will detect changes and import

9.3 Model Import Settings (Inspector)
-------------------------------------
Click the model file (e.g., character.fbx) and set:

A) Model tab
- Scale Factor: usually 1 (adjust only if needed)
- Read/Write Enabled: keep OFF unless you truly need it
- Mesh Compression: Off / Low (start with Off for safety)
Click Apply after changes.

B) Rig tab (characters)
- Animation Type: Humanoid (human) / Generic (non-human)
- Click Apply
- If Humanoid: click Configure and make sure bones map correctly

C) Animation tab (if model has animations)
- Enable Import Animation
- Split clips (idle/walk/run) if needed
Click Apply.

D) Materials tab (common issue)
- Extract Materials… → choose Assets/Art/Materials/
- If textures are embedded or missing: Extract Textures… → Assets/Art/Textures/
- If materials look wrong in URP: you may need URP shader materials

9.4 Make Prefabs (best practice)
--------------------------------
Do NOT keep dragging raw models into scenes every time.
1) Drag the model into the scene once
2) Add components (Collider, scripts, materials)
3) Drag that object from Hierarchy into Assets/Prefabs/ to create a Prefab
4) Use the Prefab in scenes from now on

------------------------------------------------------------
10) Commit Imported Models (Use a feature branch)
------------------------------------------------------------
Recommended workflow:
1) Create a branch:
   git checkout -b feature/import-models

2) Add and commit:
   git add Assets/Art Assets/Prefabs
   git commit -m "Import models and create prefabs"
   git push -u origin feature/import-models

3) Open a Pull Request on GitHub → merge into main

------------------------------------------------------------
11) Daily Git Workflow (Team)
------------------------------------------------------------
Start work:
git checkout main
git pull
git checkout -b feature/your-task

Commit often (small commits):
git add -A
git commit -m "Short description of change"
git push -u origin feature/your-task

Open PR → review → merge.

------------------------------------------------------------
12) Unity Merge Conflict Tips
------------------------------------------------------------
Most conflicts happen in:
- Scenes (*.unity)
- Prefabs (*.prefab)
- ProjectSettings files

Ways to reduce conflicts:
- Don’t have two people edit the same scene at the same time
- Use prefabs; keep scenes small
- Merge frequently (daily)
- Communicate when someone is editing a critical file

------------------------------------------------------------
END
------------------------------------------------------------

Replace placeholders:
- [<REPOSITORY_URL>](https://github.com/dyu55/Seeding-Chains-of-Life.git)
- [<REPOSITORY_FOLDER_NAME> ]Seeding-Chains-of-Life

- Unity version in section 1
