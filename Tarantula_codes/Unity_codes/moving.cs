using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

public class moving : MonoBehaviour
{
    public enum MoveAxis
    {
        WorldZ,
        WorldMinusZ,
        WorldX,
        WorldMinusX,
        LocalZ,
        LocalMinusZ,
        LocalX,
        LocalMinusX
    }

    enum Command
    {
        Stop,
        Forward,
        Backward,
        Left,
        Right
    }

    [Header("Robot Root")]
    public Transform robotRoot;

    [Header("TCP Server")]
    public bool startTcpServer = true;
    public int port = 7777;

    [Header("Body Movement")]
    public float moveSpeed = 10f;
    public float turnSpeed = 5f;
    public MoveAxis forwardAxis = MoveAxis.LocalZ;
    public bool keepYPosition = true;
    public bool useUnscaledTime = false;

    [Header("Keyboard Test")]
    public bool keyboardTest = true;

    [Header("Leg Servo Transforms")]
    public Transform[] servos = new Transform[12];

    [Header("Leg Animation")]
    public bool animateLegs = true;
    public bool useDemoGaitWhenNoServoData = true;
    public float externalServoTimeout = 0.6f;
    public float legSmoothSpeed = 10f;
    public float gaitStepInterval = 0.22f;

    [Header("Leg Angles")]
    public float shoulderNeutral = 90f;
    public float kneeNeutral = 70f;
    public float kneeUp = 115f;
    public float kneeDown = 50f;
    public float shoulderForward = 110f;
    public float shoulderBackward = 70f;

    [Header("Servo Rotation Axis")]
    public Vector3 shoulderAxis = Vector3.up;
    public Vector3 kneeAxis = Vector3.right;

    [Header("Status")]
    public string lastCommand = "STOP";
    public bool isAutonomousMode = false;
    public bool clientConnected = false;
    public Vector3 currentPosition;
    public float[] targetAngles = new float[12];
    public float[] currentAngles = new float[12];

    [Header("Leg Source Debug")]
    public bool showLegSourceDebug = true;
    public string legAngleSource = "NONE";
    public int servoPacketCount = 0;
    public int demoGaitStepCount = 0;
    public float sourceDebugInterval = 1f;

    Command currentCommand = Command.Stop;

    TcpListener listener;
    TcpClient client;
    NetworkStream stream;
    Thread serverThread;
    bool running = false;

    readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    float gaitTimer = 0f;
    int gaitPhase = 0;
    float lastServoPacketTime = -999f;
    float sourceDebugTimer = 0f;

    static readonly bool[] isShoulder =
    {
        true,  false,
        false, true,
        true,  false,
        true,  false,
        true,  false,
        true,  false
    };

    static readonly bool[] isLeftSide =
    {
        false, false,
        false, false,
        false, false,
        true,  true,
        true,  true,
        true,  true
    };

    readonly int[] groupA = { 0, 2, 4 };
    readonly int[] groupB = { 3, 5, 1 };

    readonly int[] legShoulderCh = { 0, 3, 4, 6, 8, 10 };
    readonly int[] legKneeCh = { 1, 2, 5, 7, 9, 11 };

    void Start()
    {
        if (robotRoot == null)
            robotRoot = transform;

        InitializeAngles();

        if (startTcpServer)
            StartServer();
    }

    void Update()
    {
        ReadNetworkMessages();

        if (keyboardTest)
            ReadKeyboardInput();

        MoveRobot();

        if (animateLegs)
            UpdateLegAnimation();

        UpdateServoRotations();
        UpdateLegSourceDebug();

        if (robotRoot != null)
            currentPosition = robotRoot.position;
    }

    void InitializeAngles()
    {
        if (targetAngles == null || targetAngles.Length != 12)
            targetAngles = new float[12];

        if (currentAngles == null || currentAngles.Length != 12)
            currentAngles = new float[12];

        SetLegsNeutral();

        for (int i = 0; i < 12; i++)
            currentAngles[i] = targetAngles[i];
    }

    void StartServer()
    {
        running = true;

        serverThread = new Thread(ServerLoop);
        serverThread.IsBackground = true;
        serverThread.Start();
    }

    void ServerLoop()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (running)
            {
                client = listener.AcceptTcpClient();
                clientConnected = true;
                stream = client.GetStream();

                byte[] buffer = new byte[1024];
                StringBuilder pending = new StringBuilder();

                while (running && client != null && client.Connected)
                {
                    int count = stream.Read(buffer, 0, buffer.Length);

                    if (count <= 0)
                        break;

                    string incoming = Encoding.UTF8.GetString(buffer, 0, count);
                    incoming = incoming.Replace("\0", "");

                    pending.Append(incoming);
                    string all = pending.ToString();

                    while (all.Contains("\n"))
                    {
                        int index = all.IndexOf('\n');
                        string line = all.Substring(0, index).Trim();
                        all = all.Substring(index + 1);

                        if (!string.IsNullOrWhiteSpace(line))
                            messageQueue.Enqueue(line);
                    }

                    string possible = all.Trim();

                    if (IsCommandText(possible) || possible.StartsWith("SERVO:", StringComparison.OrdinalIgnoreCase))
                    {
                        messageQueue.Enqueue(possible);
                        all = "";
                    }

                    pending.Clear();
                    pending.Append(all);
                }

                clientConnected = false;
            }
        }
        catch
        {
            clientConnected = false;
        }
    }

    void ReadNetworkMessages()
    {
        while (messageQueue.TryDequeue(out string msg))
        {
            ApplyIncomingMessage(msg);
        }
    }

    void ReadKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            ApplyIncomingMessage("F");

        if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            ApplyIncomingMessage("B");

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            ApplyIncomingMessage("L");

        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            ApplyIncomingMessage("R");

        if (Input.GetKeyDown(KeyCode.G))
            ApplyIncomingMessage("G");

        if (Input.GetKeyDown(KeyCode.Space))
            ApplyIncomingMessage("STOP");

        if (Input.GetKeyDown(KeyCode.Escape))
            ApplyIncomingMessage("ABORT");
    }

    void ApplyIncomingMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string clean = text.Trim();

        if (clean.StartsWith("SERVO:", StringComparison.OrdinalIgnoreCase))
        {
            ParseServoPacket(clean);
            return;
        }

        ApplyCommand(clean);
    }

    void ApplyCommand(string text)
    {
        string cmd = NormalizeCommand(text);

        if (string.IsNullOrWhiteSpace(cmd))
            return;

        if (cmd == "FORWARD")
        {
            isAutonomousMode = false;
            currentCommand = Command.Forward;
            lastCommand = "FORWARD";
        }
        else if (cmd == "BACKWARD")
        {
            isAutonomousMode = false;
            currentCommand = Command.Backward;
            lastCommand = "BACKWARD";
        }
        else if (cmd == "LEFT")
        {
            isAutonomousMode = false;
            currentCommand = Command.Left;
            lastCommand = "LEFT";
        }
        else if (cmd == "RIGHT")
        {
            isAutonomousMode = false;
            currentCommand = Command.Right;
            lastCommand = "RIGHT";
        }
        else if (cmd == "STOP")
        {
            isAutonomousMode = false;
            currentCommand = Command.Stop;
            lastCommand = "STOP";
            SetLegsNeutral();
        }
        else if (cmd == "AUTONOMOUS")
        {
            isAutonomousMode = true;
            currentCommand = Command.Forward;
            lastCommand = "AUTONOMOUS";
        }
        else if (cmd == "ABORT")
        {
            isAutonomousMode = false;
            currentCommand = Command.Stop;
            lastCommand = "ABORT";
            SetLegsNeutral();
        }
    }

    string NormalizeCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string cmd = text.Trim().ToUpper();

        cmd = cmd.Replace("\0", "");
        cmd = cmd.Replace("\r", "");
        cmd = cmd.Replace("\n", "");
        cmd = cmd.Replace("\t", "");
        cmd = cmd.Replace(" ", "");
        cmd = cmd.Replace("\"", "");
        cmd = cmd.Replace("'", "");
        cmd = cmd.Replace("{", "");
        cmd = cmd.Replace("}", "");
        cmd = cmd.Replace("[", "");
        cmd = cmd.Replace("]", "");

        if (cmd.Contains("CMD:F")) return "FORWARD";
        if (cmd.Contains("CMD:W")) return "FORWARD";
        if (cmd.Contains("CMD:B")) return "BACKWARD";
        if (cmd.Contains("CMD:X")) return "BACKWARD";
        if (cmd.Contains("CMD:L")) return "LEFT";
        if (cmd.Contains("CMD:A")) return "LEFT";
        if (cmd.Contains("CMD:R")) return "RIGHT";
        if (cmd.Contains("CMD:D")) return "RIGHT";
        if (cmd.Contains("CMD:S")) return "STOP";
        if (cmd.Contains("CMD:STOP")) return "STOP";
        if (cmd.Contains("CMD:G")) return "AUTONOMOUS";
        if (cmd.Contains("CMD:START")) return "AUTONOMOUS";
        if (cmd.Contains("CMD:ESC")) return "ABORT";

        if (cmd.Contains("FORWARD")) return "FORWARD";
        if (cmd.Contains("BACKWARD")) return "BACKWARD";
        if (cmd.Contains("BACK")) return "BACKWARD";
        if (cmd.Contains("LEFT")) return "LEFT";
        if (cmd.Contains("RIGHT")) return "RIGHT";
        if (cmd.Contains("STOP")) return "STOP";
        if (cmd.Contains("START")) return "AUTONOMOUS";
        if (cmd.Contains("AUTONOM")) return "AUTONOMOUS";
        if (cmd.Contains("ABORT")) return "ABORT";
        if (cmd.Contains("CANCEL")) return "ABORT";
        if (cmd.Contains("ESC")) return "ABORT";

        if (cmd.Contains("ILERI")) return "FORWARD";
        if (cmd.Contains("İLERİ")) return "FORWARD";
        if (cmd.Contains("GERI")) return "BACKWARD";
        if (cmd.Contains("GERİ")) return "BACKWARD";
        if (cmd.Contains("SOL")) return "LEFT";
        if (cmd.Contains("SAG")) return "RIGHT";
        if (cmd.Contains("SAĞ")) return "RIGHT";
        if (cmd.Contains("DUR")) return "STOP";
        if (cmd.Contains("OTONOM")) return "AUTONOMOUS";
        if (cmd.Contains("IPTAL")) return "ABORT";
        if (cmd.Contains("İPTAL")) return "ABORT";

        if (cmd == "F") return "FORWARD";
        if (cmd == "W") return "FORWARD";
        if (cmd == "B") return "BACKWARD";
        if (cmd == "X") return "BACKWARD";
        if (cmd == "L") return "LEFT";
        if (cmd == "A") return "LEFT";
        if (cmd == "R") return "RIGHT";
        if (cmd == "D") return "RIGHT";
        if (cmd == "S") return "STOP";
        if (cmd == "SPACE") return "STOP";
        if (cmd == "G") return "AUTONOMOUS";

        return cmd;
    }

    bool IsCommandText(string text)
    {
        string cmd = NormalizeCommand(text);

        return cmd == "FORWARD" ||
               cmd == "BACKWARD" ||
               cmd == "LEFT" ||
               cmd == "RIGHT" ||
               cmd == "STOP" ||
               cmd == "AUTONOMOUS" ||
               cmd == "ABORT";
    }

    void ParseServoPacket(string line)
    {
        string payload = line.Substring(6);
        string[] parts = payload.Split(',');

        if (parts.Length < 12)
            return;

        for (int i = 0; i < 12; i++)
        {
            if (float.TryParse(
                parts[i],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float value))
            {
                targetAngles[i] = value;
            }
        }

        lastServoPacketTime = Time.time;
        servoPacketCount++;
        legAngleSource = "REAL SERVO DATA";

        if (showLegSourceDebug)
        {
            Debug.Log("[LEG SOURCE] REAL SERVO DATA received | PacketCount=" + servoPacketCount + " | Angles=" + payload);
        }
    }

    Vector3 GetForwardDirection()
    {
        if (robotRoot == null)
            return Vector3.forward;

        Vector3 dir = Vector3.forward;

        if (forwardAxis == MoveAxis.WorldZ)
            dir = Vector3.forward;
        else if (forwardAxis == MoveAxis.WorldMinusZ)
            dir = Vector3.back;
        else if (forwardAxis == MoveAxis.WorldX)
            dir = Vector3.right;
        else if (forwardAxis == MoveAxis.WorldMinusX)
            dir = Vector3.left;
        else if (forwardAxis == MoveAxis.LocalZ)
            dir = robotRoot.forward;
        else if (forwardAxis == MoveAxis.LocalMinusZ)
            dir = -robotRoot.forward;
        else if (forwardAxis == MoveAxis.LocalX)
            dir = robotRoot.right;
        else if (forwardAxis == MoveAxis.LocalMinusX)
            dir = -robotRoot.right;

        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;

        return dir.normalized;
    }

    void MoveRobot()
    {
        if (robotRoot == null)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        Vector3 before = robotRoot.position;
        Vector3 moveDirection = GetForwardDirection();

        if (currentCommand == Command.Forward)
        {
            robotRoot.position += moveDirection * moveSpeed * dt;
        }
        else if (currentCommand == Command.Backward)
        {
            robotRoot.position -= moveDirection * moveSpeed * dt;
        }
        else if (currentCommand == Command.Left)
        {
            robotRoot.Rotate(0f, -turnSpeed * dt, 0f, Space.World);
        }
        else if (currentCommand == Command.Right)
        {
            robotRoot.Rotate(0f, turnSpeed * dt, 0f, Space.World);
        }

        if (keepYPosition)
        {
            Vector3 p = robotRoot.position;
            p.y = before.y;
            robotRoot.position = p;
        }
    }

    void UpdateLegAnimation()
    {
        bool hasRecentServoData = (Time.time - lastServoPacketTime) <= externalServoTimeout;

        if (hasRecentServoData)
        {
            legAngleSource = "REAL SERVO DATA";
            return;
        }

        if (!useDemoGaitWhenNoServoData)
        {
            legAngleSource = "NO SERVO DATA - DEMO DISABLED";
            return;
        }

        if (currentCommand == Command.Stop)
        {
            legAngleSource = "STOP - NEUTRAL";
            return;
        }

        legAngleSource = "DEMO GAIT";

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        gaitTimer += dt;

        if (gaitTimer < gaitStepInterval)
            return;

        gaitTimer = 0f;

        if (currentCommand == Command.Forward || isAutonomousMode)
            ForwardGait();
        else if (currentCommand == Command.Backward)
            BackwardGait();
        else if (currentCommand == Command.Left)
            LeftTurnGait();
        else if (currentCommand == Command.Right)
            RightTurnGait();

        gaitPhase++;

        if (gaitPhase > 5)
            gaitPhase = 0;

        demoGaitStepCount++;

        if (showLegSourceDebug)
        {
            Debug.Log("[LEG SOURCE] DEMO GAIT used | StepCount=" + demoGaitStepCount +
                      " | Command=" + lastCommand +
                      " | Phase=" + gaitPhase);
        }
    }

    void ForwardGait()
    {
        switch (gaitPhase)
        {
            case 0:
                LiftGroup(groupA);
                LowerGroup(groupB);
                break;

            case 1:
                MoveShoulders(groupA, shoulderForward);
                MoveShoulders(groupB, shoulderBackward);
                break;

            case 2:
                LowerGroup(groupA);
                break;

            case 3:
                LiftGroup(groupB);
                LowerGroup(groupA);
                break;

            case 4:
                MoveShoulders(groupB, shoulderForward);
                MoveShoulders(groupA, shoulderBackward);
                break;

            case 5:
                LowerGroup(groupB);
                break;
        }
    }

    void BackwardGait()
    {
        switch (gaitPhase)
        {
            case 0:
                LiftGroup(groupA);
                LowerGroup(groupB);
                break;

            case 1:
                MoveShoulders(groupA, shoulderBackward);
                MoveShoulders(groupB, shoulderForward);
                break;

            case 2:
                LowerGroup(groupA);
                break;

            case 3:
                LiftGroup(groupB);
                LowerGroup(groupA);
                break;

            case 4:
                MoveShoulders(groupB, shoulderBackward);
                MoveShoulders(groupA, shoulderForward);
                break;

            case 5:
                LowerGroup(groupB);
                break;
        }
    }

    void LeftTurnGait()
    {
        switch (gaitPhase)
        {
            case 0:
                LiftGroup(groupA);
                LowerGroup(groupB);
                break;

            case 1:
                TurnShouldersLeft(groupA);
                TurnShouldersLeft(groupB);
                break;

            case 2:
                LowerGroup(groupA);
                break;

            case 3:
                LiftGroup(groupB);
                LowerGroup(groupA);
                break;

            case 4:
                TurnShouldersLeft(groupB);
                TurnShouldersLeft(groupA);
                break;

            case 5:
                LowerGroup(groupB);
                break;
        }
    }

    void RightTurnGait()
    {
        switch (gaitPhase)
        {
            case 0:
                LiftGroup(groupA);
                LowerGroup(groupB);
                break;

            case 1:
                TurnShouldersRight(groupA);
                TurnShouldersRight(groupB);
                break;

            case 2:
                LowerGroup(groupA);
                break;

            case 3:
                LiftGroup(groupB);
                LowerGroup(groupA);
                break;

            case 4:
                TurnShouldersRight(groupB);
                TurnShouldersRight(groupA);
                break;

            case 5:
                LowerGroup(groupB);
                break;
        }
    }

    void SetLegsNeutral()
    {
        for (int i = 0; i < 12; i++)
            targetAngles[i] = shoulderNeutral;

        targetAngles[1] = kneeNeutral;
        targetAngles[2] = kneeNeutral;
        targetAngles[5] = kneeNeutral;
        targetAngles[7] = kneeNeutral;
        targetAngles[9] = kneeNeutral;
        targetAngles[11] = kneeNeutral;
    }

    void LiftGroup(int[] group)
    {
        foreach (int leg in group)
        {
            int kneeCh = legKneeCh[leg];
            targetAngles[kneeCh] = kneeUp;
        }
    }

    void LowerGroup(int[] group)
    {
        foreach (int leg in group)
        {
            int kneeCh = legKneeCh[leg];
            targetAngles[kneeCh] = kneeDown;
        }
    }

    void MoveShoulders(int[] group, float angle)
    {
        foreach (int leg in group)
        {
            int shoulderCh = legShoulderCh[leg];
            targetAngles[shoulderCh] = angle;
        }
    }

    void TurnShouldersLeft(int[] group)
    {
        foreach (int leg in group)
        {
            int shoulderCh = legShoulderCh[leg];

            if (isLeftSide[shoulderCh])
                targetAngles[shoulderCh] = shoulderBackward;
            else
                targetAngles[shoulderCh] = shoulderForward;
        }
    }

    void TurnShouldersRight(int[] group)
    {
        foreach (int leg in group)
        {
            int shoulderCh = legShoulderCh[leg];

            if (isLeftSide[shoulderCh])
                targetAngles[shoulderCh] = shoulderForward;
            else
                targetAngles[shoulderCh] = shoulderBackward;
        }
    }

    void UpdateServoRotations()
    {
        if (servos == null)
            return;

        for (int i = 0; i < 12; i++)
        {
            if (i >= servos.Length)
                continue;

            if (servos[i] == null)
                continue;

            currentAngles[i] = Mathf.LerpAngle(
                currentAngles[i],
                targetAngles[i],
                Time.deltaTime * legSmoothSpeed
            );

            ApplyServoRotation(i, currentAngles[i]);
        }
    }

    void ApplyServoRotation(int ch, float angle)
    {
        float delta = angle - 90f;
        Quaternion rot;

        if (isShoulder[ch])
        {
            float sign = isLeftSide[ch] ? -1f : 1f;
            rot = Quaternion.AngleAxis(sign * delta, shoulderAxis);
        }
        else
        {
            rot = Quaternion.AngleAxis(delta, kneeAxis);
        }

        servos[ch].localRotation = Quaternion.Lerp(
            servos[ch].localRotation,
            rot,
            Time.deltaTime * legSmoothSpeed
        );
    }

    void UpdateLegSourceDebug()
    {
        if (!showLegSourceDebug)
            return;

        sourceDebugTimer += Time.deltaTime;

        if (sourceDebugTimer < sourceDebugInterval)
            return;

        sourceDebugTimer = 0f;

        bool hasRecentServoData = (Time.time - lastServoPacketTime) <= externalServoTimeout;

        string activeSource;

        if (hasRecentServoData)
            activeSource = "REAL SERVO DATA";
        else if (currentCommand != Command.Stop && useDemoGaitWhenNoServoData)
            activeSource = "DEMO GAIT";
        else if (currentCommand == Command.Stop)
            activeSource = "STOP / NEUTRAL";
        else
            activeSource = "NO ACTIVE LEG SOURCE";

        legAngleSource = activeSource;

        Debug.Log("[LEG DEBUG] Source=" + activeSource +
                  " | LastCommand=" + lastCommand +
                  " | ServoPackets=" + servoPacketCount +
                  " | DemoSteps=" + demoGaitStepCount +
                  " | ServoRecent=" + hasRecentServoData);
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    void OnDisable()
    {
        StopServer();
    }

    void StopServer()
    {
        running = false;
        clientConnected = false;

        try
        {
            if (stream != null)
                stream.Close();
        }
        catch { }

        try
        {
            if (client != null)
                client.Close();
        }
        catch { }

        try
        {
            if (listener != null)
                listener.Stop();
        }
        catch { }
    }
}