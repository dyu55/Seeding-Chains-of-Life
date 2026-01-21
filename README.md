# Seeding-Chains-of-Life
Game Overview
-------------
Project Title is a Unity-based game developed as a team project.
Briefly describe the gameplay, theme, or goal of the game here.

Tech Stack
----------
Engine: Unity (exact version must match for all team members)
Language: C#
Rendering Pipeline: URP / Built-in
Version Control: Git + GitHub
Large File Support: Git LFS

Project Structure
-----------------
Assets/            Game assets, scripts, scenes
Packages/          Unity package dependencies
ProjectSettings/   Unity project configuration
Docs/              Design docs, sprint plans, references

Note: Unity-generated folders such as Library/, Temp/, and Build/ are ignored via .gitignore.

Getting Started
---------------
Prerequisites:
- Unity Hub installed
- Same Unity Editor version for everyone
- Git installed
- GitHub account
- Git LFS installed (required)

Clone the repository:
git clone <repository-url>
cd <repository-name>

Git LFS Setup (Required)
-----------------------
Unity projects contain large binary assets (textures, audio, models).
Git LFS must be installed before committing assets.

macOS (Homebrew recommended):
brew install git-lfs
git lfs install

Verify installation:
git lfs version

If Homebrew is not installed:
Install Homebrew first:
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

Then install Git LFS:
brew install git-lfs
git lfs install

Track Unity asset types:
git lfs track "*.psd" "*.png" "*.jpg" "*.jpeg" "*.tga"
git lfs track "*.wav" "*.mp3" "*.ogg"
git lfs track "*.fbx" "*.blend"

Commit LFS tracking:
git add .gitattributes
git commit -m "Configure Git LFS tracking for Unity assets"

Unity Project Settings (Important)
---------------------------------
In Unity:
Edit → Project Settings → Editor

Set:
Version Control Mode: Visible Meta Files
Asset Serialization Mode: Force Text

These settings reduce merge conflicts and prevent broken asset references.

Git Workflow
------------
main branch: stable, playable builds only
Feature branches: feature/short-description
Pull Requests required before merging into main

Create a feature branch:
git checkout -b feature/player-movement

Unity + Git Rules
-----------------
- Always commit .meta files
- Never commit Library/, Temp/, or Build/
- Avoid multiple people editing the same scene simultaneously
- Prefer prefabs over large monolithic scenes
- Commit small, focused changes

Team
----
Producer / Designer:
Programmer:
Artist:

License
-------
Specify license here (or remove this section if not applicable).

Notes
-----
- Git LFS must be installed before committing assets
- All contributors must use the same Unity version
- Large binary files should never be committed without LFS


TEAM UNITY PROJECT SETUP INSTRUCTIONS
====================================

These instructions explain how to import and work on the Unity project
from GitHub on your own computer.


1. Install Required Software
----------------------------
Before starting, make sure you have:

- Unity Hub installed
- The SAME Unity Editor version as the team (check README)
- Git installed
- Git LFS installed (required for assets)


2. Install Git LFS
------------------
Git LFS is required to download large Unity assets correctly.

macOS (Homebrew recommended):
brew install git-lfs
git lfs install

Windows:
- Install Git for Windows
- Install Git LFS (often included)
Then run:
git lfs install

Verify installation:
git lfs version


3. Clone the GitHub Repository
------------------------------
Open Terminal (macOS) or Git Bash / PowerShell (Windows).

Navigate to a folder where you keep projects, then run:

git clone <REPOSITORY_URL>
cd <REPOSITORY_FOLDER_NAME>


4. Pull Git LFS Files (IMPORTANT)
---------------------------------
After cloning, run:

git lfs pull

This ensures large assets (textures, audio, models) are downloaded.


5. Open the Project in Unity Hub
--------------------------------
1. Open Unity Hub
2. Click "Open" or "Add"
3. Select the repository folder
   (the folder containing Assets/, Packages/, ProjectSettings/)
4. If prompted, install the correct Unity version
5. Click the project to open it


6. First-Time Project Check
---------------------------
When the project opens for the first time:

- Wait for Unity to finish importing
- Make sure there are no errors in the Console
- Open the main scene (e.g. Assets/Scenes/MainScene.unity)
- Press Play once to confirm it runs


7. Working on the Project (Git Workflow)
----------------------------------------
Do NOT work directly on the main branch.

Create a feature branch:
git checkout -b feature/your-task-name

After making changes:
git add -A
git commit -m "Describe what you changed"
git push -u origin feature/your-task-name

Then open a Pull Request on GitHub to merge into main.


8. Important Unity + Git Rules
------------------------------
- Always commit .meta files
- Never commit Library/, Temp/, Obj/, or Build/ folders
- Avoid multiple people editing the same scene at the same time
- Prefer prefabs over large single scenes
- Commit small, focused changes frequently


If you run into issues:
- Check that your Unity version matches the team version
- Make sure Git LFS is installed and git lfs pull was run
- Ask the team before fixing merge conflicts
