using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Cutscene
{
    public string id;
    public string startCamera;
    public List<CutsceneStep> steps;
}

[Serializable]
public class CutsceneStep
{
    public string id;
    public string type;
    public string speaker;
    public string portrait;
    public string text;
    public string animation;
    public float zoom;
    public float panX;
    public float panY;
    public float duration;
    public float seconds;
}