using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class ThreadedDataRequester : MonoBehaviour
{
    private static ThreadedDataRequester instance;
    private Queue<ThreadInfo> dataQueue = new Queue<ThreadInfo>();

    void Awake()
    {
        instance = FindObjectOfType<ThreadedDataRequester>();
    }

    public static void RequestData(Func<object> generateData, Action<object> callback)
    {
        ThreadStart threadStart = delegate
        {
            instance.DataThread(generateData, callback);
        };

        new Thread(threadStart).Start();
    }

    void DataThread(Func<object> generateData, Action<object> callback)
    {
        object data = generateData();
        lock (dataQueue)
        {
            dataQueue.Enqueue(new ThreadInfo(callback, data));
        }
    }

    void Update()
    {
        if (dataQueue.Count > 0)
        {
            for (int i = 0; i < dataQueue.Count; i++)
            {
                ThreadInfo threadInfo = dataQueue.Dequeue();
                threadInfo.callback(threadInfo.parameter);
            }
        }
    }

    struct ThreadInfo
    {
        public Action<object> callback;
        public object parameter;

        public ThreadInfo(Action<object> callback, object parameter) : this()
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }
}
