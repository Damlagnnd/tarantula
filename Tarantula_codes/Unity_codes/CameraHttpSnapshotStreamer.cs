using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class CameraHttpSnapshotStreamer : MonoBehaviour
{
    [Header("Camera Source")]
    public Camera sourceCamera;

    [Header("HTTP Live Stream Settings")]
    public int port = 8080;
    public int width = 640;
    public int height = 360;
    public int fps = 15;

    [Range(10, 100)]
    public int jpgQuality = 70;

    [Header("Debug")]
    public bool showLogs = true;
    public string streamUrl = "";
    public int frameCount = 0;
    public int clientCount = 0;

    RenderTexture renderTexture;
    Texture2D captureTexture;

    TcpListener listener;
    Thread serverThread;
    bool running = false;

    byte[] latestJpg;
    readonly object jpgLock = new object();

    readonly List<TcpClient> clients = new List<TcpClient>();
    readonly object clientsLock = new object();

    void Start()
    {
        if (sourceCamera == null)
            sourceCamera = GetComponent<Camera>();

        if (sourceCamera == null)
        {
            Debug.LogError("[Camera Stream] Source Camera yok. Scripti Main Camera'ya ekle.");
            enabled = false;
            return;
        }

        renderTexture = new RenderTexture(width, height, 24);
        captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

        streamUrl = "http://127.0.0.1:" + port + "/stream";

        running = true;

        StartCoroutine(CaptureLoop());

        serverThread = new Thread(ServerLoop);
        serverThread.IsBackground = true;
        serverThread.Start();

        if (showLogs)
        {
            Debug.Log("[Camera Stream] Live MJPEG stream started.");
            Debug.Log("[Camera Stream] Source Camera: " + sourceCamera.name);
            Debug.Log("[Camera Stream] Port: " + port);
            Debug.Log("[Camera Stream] URL: " + streamUrl);
        }
    }

    IEnumerator CaptureLoop()
    {
        float delay = 1f / Mathf.Max(1, fps);

        while (running)
        {
            yield return new WaitForEndOfFrame();

            CaptureFrame();

            if (delay > 0f)
                yield return new WaitForSeconds(delay);
        }
    }

    void CaptureFrame()
    {
        if (sourceCamera == null)
            return;

        RenderTexture oldTarget = sourceCamera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;

        sourceCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;

        sourceCamera.Render();

        captureTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        captureTexture.Apply();

        byte[] jpg = captureTexture.EncodeToJPG(jpgQuality);

        lock (jpgLock)
        {
            latestJpg = jpg;
        }

        sourceCamera.targetTexture = oldTarget;
        RenderTexture.active = oldActive;

        frameCount++;
    }

    void ServerLoop()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (running)
            {
                TcpClient client = listener.AcceptTcpClient();

                lock (clientsLock)
                {
                    clients.Add(client);
                    clientCount = clients.Count;
                }

                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }
        catch (Exception e)
        {
            if (running && showLogs)
                Debug.LogError("[Camera Stream] Server error: " + e.Message);
        }
    }

    void HandleClient(TcpClient client)
    {
        NetworkStream stream = null;

        try
        {
            stream = client.GetStream();

            string header =
                "HTTP/1.1 200 OK\r\n" +
                "Server: UnityCameraStream\r\n" +
                "Connection: close\r\n" +
                "Max-Age: 0\r\n" +
                "Expires: 0\r\n" +
                "Cache-Control: no-cache, private\r\n" +
                "Pragma: no-cache\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Flush();

            int sleepMs = Mathf.Max(1, 1000 / Mathf.Max(1, fps));

            while (running && client.Connected)
            {
                byte[] frame;

                lock (jpgLock)
                {
                    frame = latestJpg;
                }

                if (frame != null && frame.Length > 0)
                {
                    string frameHeader =
                        "--frame\r\n" +
                        "Content-Type: image/jpeg\r\n" +
                        "Content-Length: " + frame.Length + "\r\n\r\n";

                    byte[] frameHeaderBytes = Encoding.ASCII.GetBytes(frameHeader);

                    stream.Write(frameHeaderBytes, 0, frameHeaderBytes.Length);
                    stream.Write(frame, 0, frame.Length);

                    byte[] newLine = Encoding.ASCII.GetBytes("\r\n");
                    stream.Write(newLine, 0, newLine.Length);

                    stream.Flush();
                }

                Thread.Sleep(sleepMs);
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                if (stream != null)
                    stream.Close();
            }
            catch { }

            try
            {
                client.Close();
            }
            catch { }

            lock (clientsLock)
            {
                clients.Remove(client);
                clientCount = clients.Count;
            }
        }
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

        lock (clientsLock)
        {
            foreach (TcpClient c in clients)
            {
                try
                {
                    c.Close();
                }
                catch { }
            }

            clients.Clear();
            clientCount = 0;
        }

        try
        {
            if (listener != null)
                listener.Stop();
        }
        catch { }

        try
        {
            if (renderTexture != null)
                renderTexture.Release();
        }
        catch { }
    }
}