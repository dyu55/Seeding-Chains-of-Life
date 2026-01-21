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
