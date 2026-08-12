using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DynamicReverbSystem : MonoBehaviour
{
    [Header("Настройки скана")]
    [Tooltip("LayerMask для стен/пола/потолка")]
    public LayerMask wallLayer = -1;
    [Tooltip("Минимальное расстояние для игнорирования коллайдера игрока")]
    public float minRaycastDistance = 1f;
    [Tooltip("Максимальная дистанция рейкаста")]
    public float maxRaycastDistance = 100f;
    [Tooltip("Скорость плавной интерполяции параметров")]
    [Range(0.1f, 5f)]
    public float lerpSpeed = 3f;

    [Header("Расширенные настройки")]
    [Tooltip("Количество лучей для сканирования (6-100)")]
    [Range(6, 100)]
    public int rayCount = 18;
    [Tooltip("Использовать асинхронный скан через Coroutine (не блокирует кадр)")]
    public bool useAsyncScan = false;

    [Header("Adaptive Scan Rate")]
    [Tooltip("Включить умную частоту сканирования по скорости игрока")]
    public bool useAdaptiveScanRate = true;
    [Tooltip("Интервал скана когда игрок стоит (speed < idleSpeedThreshold)")]
    [Range(0.5f, 5f)]
    public float idleScanInterval = 2f;
    [Tooltip("Интервал скана при обычной ходьбе")]
    [Range(0.25f, 2f)]
    public float walkScanInterval = 0.5f;
    [Tooltip("Интервал скана когда игрок бежит (speed > runSpeedThreshold)")]
    [Range(0.1f, 1f)]
    public float runScanInterval = 0.2f;
    [Tooltip("Порог скорости для определения «стоит» (м/с)")]
    public float idleSpeedThreshold = 0.1f;
    [Tooltip("Порог скорости для определения «бежит» (м/с)")]
    public float runSpeedThreshold = 3f;
    [Tooltip("Если позиция изменилась больше этого значения за 1 кадр — принудительный скан (телепорт/дверь)")]
    public float teleportThreshold = 0.5f;
    [Tooltip("Порог дельты для определения резкого перехода (0–1, где 1 = maxReferenceVolume)")]
    [Range(0.05f, 0.5f)]
    public float transitionThreshold = 0.15f;
    [Tooltip("Максимальный множитель скорости при резком переходе")]
    [Range(2f, 8f)]
    public float maxTransitionMultiplier = 4f;
    [Tooltip("Время затухания множителя скорости к нормальному значению (сек)")]
    [Range(0.1f, 1f)]
    public float transitionDecayTime = 0.3f;

    [Header("Адаптивные кривые реверба")]
    [Tooltip("Максимальный объём комнаты для нормализации (м³)")]
    public float maxReferenceVolume = 1000f;
    [Tooltip("Кривая времени затухания (сек). X = нормализованный объём 0-1")]
    public AnimationCurve decayCurve = new AnimationCurve(new Keyframe[] {
        new Keyframe(0f, 0.6f),
        new Keyframe(0.1f, 1.0f),
        new Keyframe(0.3f, 1.8f),
        new Keyframe(0.5f, 2.8f),
        new Keyframe(0.7f, 4.0f),
        new Keyframe(0.9f, 5.5f),
        new Keyframe(1f, 7.0f)
    });
    [Tooltip("Кривая уровня раннего отражения (dB). X = нормализованный объём 0-1")]
    public AnimationCurve earlyLevelCurve = new AnimationCurve(new Keyframe[] {
        new Keyframe(0f, -0.5f),
        new Keyframe(0.1f, -1.0f),
        new Keyframe(0.3f, -2.0f),
        new Keyframe(0.5f, -3.0f),
        new Keyframe(0.7f, -4.5f),
        new Keyframe(0.9f, -6.5f),
        new Keyframe(1f, -8.5f)
    });
    [Tooltip("Кривая уровня позднего отражения (dB). X = нормализованный объём 0-1")]
    public AnimationCurve lateLevelCurve = new AnimationCurve(new Keyframe[] {
        new Keyframe(0f, -1.5f),
        new Keyframe(0.1f, -2.5f),
        new Keyframe(0.3f, -4.5f),
        new Keyframe(0.5f, -6.5f),
        new Keyframe(0.7f, -9.5f),
        new Keyframe(0.9f, -13.5f),
        new Keyframe(1f, -18.5f)
    });
    [Tooltip("Кривая затухания ВЧ (dB). X = нормализованный объём 0-1")]
    public AnimationCurve hfDampingCurve = new AnimationCurve(new Keyframe[] {
        new Keyframe(0f, -0.3f),
        new Keyframe(0.1f, -0.7f),
        new Keyframe(0.3f, -1.5f),
        new Keyframe(0.5f, -2.5f),
        new Keyframe(0.7f, -4.0f),
        new Keyframe(0.9f, -5.5f),
        new Keyframe(1f, -7.5f)
    });
    [Tooltip("Кривая размера комнаты (0-100). X = нормализованный объём 0-1")]
    public AnimationCurve roomSizeCurve = new AnimationCurve(new Keyframe[] {
        new Keyframe(0f, 10f),
        new Keyframe(0.1f, 15f),
        new Keyframe(0.3f, 35f),
        new Keyframe(0.5f, 55f),
        new Keyframe(0.7f, 75f),
        new Keyframe(0.9f, 90f),
        new Keyframe(1f, 100f)
    });

    [Header("Имена RTPC в Wwise")]
    public string decayRtpc = "ReverbDecay";
    public string earlyLevelRtpc = "ReverbEarlyLevel";
    public string lateLevelRtpc = "ReverbLateLevel";
    public string hfDampingRtpc = "ReverbHFDamping";
    public string roomSizeRtpc = "ReverbRoomSize";
    public string sendGainRtpc = "ReverbSendGain";
    public string externalSendGainRtpc = "ReverbSendGainExternal";
    public string roomTypeRtpc = "ReverbRoomType";
    public string panBalanceRtpc = "ReverbPanBalance";
    public string earlyDiffRtpc = "ReverbEarlyDiff";

    // текущие параметры (плавные значения для отправки в Wwise)
    private float _currentDecay;
    private float _currentEarlyLevel;
    private float _currentLateLevel;
    private float _currentHighFreqDamping;
    private float _currentSendGain;
    private float _currentExternalSendGain;
    private float _currentRoomSize;
    private float _currentRoomType;
    private float _currentPanBalance;
    private float _currentEarlyDiff;

    // целевые параметры для интерполяции
    private float _targetDecay;
    private float _targetEarlyLevel;
    private float _targetLateLevel;
    private float _targetHFDamping;
    private float _targetSendGain;
    private float _targetExternalSendGain;
    private float _targetRoomSize;
    private float _targetRoomType;
    private float _targetPanBalance;
    private float _targetEarlyDiff;

    private float _nextScanTime;
    private Camera _camera;
    private int _scanCount;
    private Vector3[] _directions;
    private bool _isScanningAsync;

    // Fade-out при выходе из помещения
    private bool _isFadingOut;
    private float _fadeOutTimer;
    private float _fadeStartDecay;
    private float _fadeStartEarlyLevel;
    private float _fadeStartLateLevel;
    private float _fadeStartHFDamping;
    private float _fadeStartSendGain;
    private float _fadeStartExternalSendGain;
    private float _fadeStartRoomSize;
    private float _fadeStartRoomType;
    private float _fadeStartPanBalance;
    private float _fadeStartEarlyDiff;

    // Adaptive Scan Rate
    private Vector3 _lastPosition;
    private float _lastSpeed;
    private float _effectiveScanInterval = 0.5f;

    // Transition Smoothing
    private float _transitionMultiplier = 1f;
    private float _transitionDecayTimer = 0f;
    private float _prevTargetDecay;
    private float _prevTargetEarlyLevel;
    private float _prevTargetLateLevel;
    private float _prevTargetHFDamping;
    private float _prevTargetSendGain;
    private float _prevTargetRoomSize;
    private float _prevTargetRoomType;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        GenerateDirections();
    }

    private void OnValidate()
    {
        GenerateDirections();
    }

    private void GenerateDirections()
    {
        int count = Mathf.Clamp(rayCount, 6, 100);
        _directions = new Vector3[count];
        
        // 6 строго осевых лучей
        _directions[0] = Vector3.right;
        _directions[1] = Vector3.left;
        _directions[2] = Vector3.up;
        _directions[3] = Vector3.down;
        _directions[4] = Vector3.forward;
        _directions[5] = Vector3.back;
        
        // Остальные распределяем равномерно по сфере (алгоритм Фибоначчи)
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        int extraCount = count - 6;
        for (int i = 0; i < extraCount; i++)
        {
            float y = 1f - (i / (float)(extraCount - 1)) * 2f;
            float radius = Mathf.Sqrt(1f - y * y);
            float theta = goldenAngle * i;
            _directions[i + 6] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius).normalized;
        }
    }

    private void Start()
    {
        _lastPosition = transform.position;
        _effectiveScanInterval = GetEffectiveScanInterval();
        _nextScanTime = Time.time + _effectiveScanInterval;
        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        _currentDecay = 0.5f;
        _currentEarlyLevel = -1.5f;
        _currentLateLevel = -3f;
        _currentHighFreqDamping = -1f;
        _currentSendGain = 0f;
        _currentExternalSendGain = 0f;
        _currentRoomSize = 10f;
        _currentRoomType = 5f;
        _currentPanBalance = 0f;
        _currentEarlyDiff = 0f;

        _targetDecay = 0.5f;
        _targetEarlyLevel = -1.5f;
        _targetLateLevel = -3f;
        _targetHFDamping = -1f;
        _targetSendGain = 0f;
        _targetExternalSendGain = 0f;
        _targetRoomSize = 10f;
        _targetRoomType = 5f;
        _targetPanBalance = 0f;
        _targetEarlyDiff = 0f;

        _prevTargetDecay = _targetDecay;
        _prevTargetEarlyLevel = _targetEarlyLevel;
        _prevTargetLateLevel = _targetLateLevel;
        _prevTargetHFDamping = _targetHFDamping;
        _prevTargetSendGain = _targetSendGain;
        _prevTargetRoomSize = _targetRoomSize;
        _prevTargetRoomType = _targetRoomType;
    }

    private void Update()
    {
        // --- Adaptive Scan Rate: считаем скорость перемещения ---
        Vector3 currentPos = transform.position;
        float frameDelta = Vector3.Distance(currentPos, _lastPosition);
        _lastSpeed = Time.deltaTime > 0f ? frameDelta / Time.deltaTime : 0f;
        bool teleportDetected = frameDelta > teleportThreshold;
        _effectiveScanInterval = GetEffectiveScanInterval();

        bool shouldScan = Time.time >= _nextScanTime || teleportDetected;

        // --- Плавное затухание при выходе из помещения ---
        if (_isFadingOut)
        {
            _fadeOutTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_fadeOutTimer / 1.5f); // Время затухания 1.5 сек
            
            if (t >= 1f)
            {
                _isFadingOut = false;
                // Достигли полной тишины — фиксируем цели
                _targetDecay = 0f;
                _targetEarlyLevel = -96f;
                _targetLateLevel = -96f;
                _targetHFDamping = -96f;
                _targetSendGain = -96f;
                _targetExternalSendGain = -96f;
                _targetRoomSize = 0f;
                _targetRoomType = -1f;
                _targetPanBalance = 0f;
                _targetEarlyDiff = 0f;
            }
            else
            {
                // Smoothstep для красивого "S-образного" затухания
                float smoothT = t * t * (3f - 2f * t);
                
                // Интерполируем от стартовых значений к "пустым"
                _targetDecay = Mathf.Lerp(_fadeStartDecay, 0f, smoothT);
                _targetEarlyLevel = Mathf.Lerp(_fadeStartEarlyLevel, -96f, smoothT);
                _targetLateLevel = Mathf.Lerp(_fadeStartLateLevel, -96f, smoothT);
                _targetHFDamping = Mathf.Lerp(_fadeStartHFDamping, -96f, smoothT);
                _targetSendGain = Mathf.Lerp(_fadeStartSendGain, -96f, smoothT);
                _targetExternalSendGain = Mathf.Lerp(_fadeStartExternalSendGain, -96f, smoothT);
                _targetRoomSize = Mathf.Lerp(_fadeStartRoomSize, 0f, smoothT);
                _targetRoomType = Mathf.Lerp(_fadeStartRoomType, -1f, smoothT);
                _targetPanBalance = Mathf.Lerp(_fadeStartPanBalance, 0f, smoothT);
                _targetEarlyDiff = Mathf.Lerp(_fadeStartEarlyDiff, 0f, smoothT);

                // Обновляем prev-значения чтобы Transition Smoothing не конфликтовал
                _prevTargetDecay = _targetDecay;
                _prevTargetEarlyLevel = _targetEarlyLevel;
                _prevTargetLateLevel = _targetLateLevel;
                _prevTargetHFDamping = _targetHFDamping;
                _prevTargetSendGain = _targetSendGain;
                _prevTargetRoomSize = _targetRoomSize;
                _prevTargetRoomType = _targetRoomType;
            }
        }

        if (shouldScan && !_isScanningAsync)
        {
            if (useAsyncScan)
            {
                _isScanningAsync = true;
                StartCoroutine(AsyncScanCoroutine());
            }
            else
            {
                // Синхронный режим: всё делаем сразу
                DoScanRoom(teleportDetected);
                ApplyInterpolation();
                SendToWwise();
                DrawDebugRays();
            }

            // Обновляем таймер для следующего скана
            _nextScanTime = Time.time + _effectiveScanInterval;
            _lastPosition = currentPos;
            DecayTransitionMultiplier();
        }

        // Применяем интерполяцию и отправляем в Wwise каждый кадр
        ApplyInterpolation();
        SendToWwise();
        DrawDebugRays();
    }

    // --- Асинхронный скан через Coroutine ---
    private IEnumerator AsyncScanCoroutine()
    {
        DoScanRoom(false);
        yield return new WaitForEndOfFrame(); // Ждём конца кадра для стабильности
        
        ApplyInterpolation();
        SendToWwise();
        DrawDebugRays();
        
        _isScanningAsync = false; // Разрешаем перезапуск только после завершения
    }

    // вычисляет интервал скана на основе скорости игрока
    private float GetEffectiveScanInterval()
    {
        if (!useAdaptiveScanRate) return walkScanInterval;

        if (_lastSpeed < idleSpeedThreshold)
            return idleScanInterval;

        if (_lastSpeed >= runSpeedThreshold)
            return runScanInterval;

        // плавная интерполяция между walk и run
        float t = Mathf.InverseLerp(idleSpeedThreshold, runSpeedThreshold, _lastSpeed);
        return Mathf.Lerp(walkScanInterval, runScanInterval, t);
    }

    // затухание множителя скорости перехода к 1
    private void DecayTransitionMultiplier()
    {
        if (_transitionMultiplier > 1f)
        {
            _transitionDecayTimer += Time.deltaTime;
            float t = _transitionDecayTimer / transitionDecayTime;
            if (t >= 1f)
            {
                _transitionMultiplier = 1f;
            }
            else
            {
                _transitionMultiplier = Mathf.Lerp(maxTransitionMultiplier, 1f, t);
            }
        }
    }

    private void DoScanRoom(bool isTeleport)
    {
        _scanCount++;
        int rayCount = _directions.Length;
        float[] distances = new float[rayCount];
        bool[] hitFlags = new bool[rayCount];

        for (int i = 0; i < rayCount; i++)
        {
            if (Physics.Raycast(transform.position, _directions[i], out RaycastHit hit, maxRaycastDistance, wallLayer))
            {
                if (hit.distance > minRaycastDistance)
                {
                    distances[i] = hit.distance;
                    hitFlags[i] = true;
                }
            }
        }

        // Группируем попадания по компонентам направления
        var upDists = new List<float>();
        var downDists = new List<float>();
        var rightDists = new List<float>();
        var leftDists = new List<float>();
        var frontDists = new List<float>();
        var backDists = new List<float>();

        for (int i = 0; i < rayCount; i++)
        {
            if (!hitFlags[i]) continue;
            Vector3 dir = _directions[i];
            float d = distances[i];

            if (dir.y > 0.7f) upDists.Add(d);
            else if (dir.y < -0.7f) downDists.Add(d);
            else if (dir.x > 0.5f) rightDists.Add(d);
            else if (dir.x < -0.5f) leftDists.Add(d);
            else if (dir.z > 0.5f) frontDists.Add(d);
            else if (dir.z < -0.5f) backDists.Add(d);
        }

        // Проверка замкнутости помещения
        bool hasCeiling = upDists.Count > 0;
        bool hasFloor = downDists.Count > 0;
        bool pairX = rightDists.Count > 0 && leftDists.Count > 0;
        bool pairZ = frontDists.Count > 0 && backDists.Count > 0;

        if (!hasCeiling || !hasFloor || (!pairX && !pairZ))
        {
            // Запоминаем текущие цели для плавного затухания
            _fadeStartDecay = _targetDecay;
            _fadeStartEarlyLevel = _targetEarlyLevel;
            _fadeStartLateLevel = _targetLateLevel;
            _fadeStartHFDamping = _targetHFDamping;
            _fadeStartSendGain = _targetSendGain;
            _fadeStartExternalSendGain = _targetExternalSendGain;
            _fadeStartRoomSize = _targetRoomSize;
            _fadeStartRoomType = _targetRoomType;
            _fadeStartPanBalance = _targetPanBalance;
            _fadeStartEarlyDiff = _targetEarlyDiff;
            
            _isFadingOut = true;
            _fadeOutTimer = 0f;
            if (_scanCount % 5 == 0)
                Debug.Log("DynamicReverb: открытое пространство (нет потолка или стен)");
            return;
        }

        // Расчёт габаритов через медиану попаданий
        float width = Median(rightDists) + Median(leftDists);
        float height = Median(upDists) + Median(downDists);
        float depth = Median(frontDists) + Median(backDists);

        if (width <= 0f || height <= 0f || depth <= 0f) return;

        // --- Сброс fade-out при обнаружении помещения ---
        if (_isFadingOut)
        {
            _isFadingOut = false;
            _fadeOutTimer = 0f;
        }

        // --- Binaural / HRTF: расчёт панорамы лево/право ---
        float leftDist = Median(leftDists);
        float rightDist = Median(rightDists);

        if (leftDist > 0f && rightDist > 0f)
        {
            float maxDist = Mathf.Max(leftDist, rightDist);
            _targetPanBalance = Mathf.Clamp((rightDist - leftDist) / maxDist, -1f, 1f);
            _targetEarlyDiff = Mathf.Clamp(20f * Mathf.Log10(rightDist / leftDist), -20f, 20f);
        }
        else
        {
            _targetPanBalance = 0f;
            _targetEarlyDiff = 0f;
        }

        // --- Распознавание типа комнаты ---
        _targetRoomType = ClassifyRoom(width, height, depth);

        float currentVolume = width * height * depth;
        float normalizedVolume = Mathf.Clamp01(currentVolume / maxReferenceVolume);

        // Вычисление целевых параметров
        _targetDecay = decayCurve.Evaluate(normalizedVolume);
        _targetEarlyLevel = earlyLevelCurve.Evaluate(normalizedVolume);
        _targetLateLevel = lateLevelCurve.Evaluate(normalizedVolume);
        _targetHFDamping = hfDampingCurve.Evaluate(normalizedVolume);
        _targetRoomSize = roomSizeCurve.Evaluate(normalizedVolume);
        _targetSendGain = -12f + (24f * normalizedVolume);
        _targetExternalSendGain = _targetSendGain;

        // --- Transition Smoothing ---
        float delta = Mathf.Abs(_targetDecay - _prevTargetDecay)
                    + Mathf.Abs(_targetEarlyLevel - _prevTargetEarlyLevel)
                    + Mathf.Abs(_targetLateLevel - _prevTargetLateLevel)
                    + Mathf.Abs(_targetHFDamping - _prevTargetHFDamping)
                    + Mathf.Abs(_targetSendGain - _prevTargetSendGain)
                    + Mathf.Abs(_targetRoomSize - _prevTargetRoomSize)
                    + Mathf.Abs(_targetRoomType - _prevTargetRoomType);

        float normalizedDelta = delta / maxReferenceVolume;

        if (normalizedDelta > transitionThreshold)
        {
            _transitionMultiplier = maxTransitionMultiplier;
            _transitionDecayTimer = 0f;
            if (_scanCount % 5 == 0)
                Debug.Log($"DynamicReverb: резкий переход (delta={normalizedDelta:F2})");
        }

        _prevTargetDecay = _targetDecay;
        _prevTargetEarlyLevel = _targetEarlyLevel;
        _prevTargetLateLevel = _targetLateLevel;
        _prevTargetHFDamping = _targetHFDamping;
        _prevTargetSendGain = _targetSendGain;
        _prevTargetRoomSize = _targetRoomSize;
        _prevTargetRoomType = _targetRoomType;

        if (_scanCount % 5 == 0)
        {
            string roomTypeName = GetRoomTypeName(_targetRoomType);
            Debug.Log($"DynamicReverb: комната \"{roomTypeName}\" (объём ~{currentVolume:F1} м³, " +
                      $"W:{width:F1} H:{height:F1} D:{depth:F1}, norm:{normalizedVolume:F2})");
        }
    }

    private float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) * 0.5f : values[mid];
    }

    private float ClassifyRoom(float width, float height, float depth)
    {
        if (height > 2f * Mathf.Max(width, depth))
            return 4f;
        if (Mathf.Max(width, depth) > 3f * Mathf.Min(width, depth))
            return 1f;
        if (Mathf.Abs(width - depth) < 2f && width > 2f * height)
            return 2f;
        if (width * depth > 4f * height * height)
            return 3f;
        if (Mathf.Abs(width - height) < 1f && Mathf.Abs(depth - height) < 1f)
            return 0f;
        return 5f;
    }

    private string GetRoomTypeName(float roomType)
    {
        switch (roomType)
        {
            case 0f: return "Куб";
            case 1f: return "Коридор";
            case 2f: return "Зал";
            case 3f: return "Ангар";
            case 4f: return "Atrium";
            case 5f: return "Комната";
            default: return "Открытое пространство";
        }
    }

    private float _lastSendGainLog = -999f;

    private void SendToWwise()
    {
        if (Mathf.Abs(_currentSendGain - _lastSendGainLog) > 0.5f)
        {
            Debug.Log($"[REVERB RTPC] SendGain={_currentSendGain:F1} | ExternalGain={_currentExternalSendGain:F1} | Room={_currentRoomType:F1}");
            _lastSendGainLog = _currentSendGain;
        }

        AkUnitySoundEngine.SetRTPCValue(decayRtpc, _currentDecay);
        AkUnitySoundEngine.SetRTPCValue(earlyLevelRtpc, _currentEarlyLevel);
        AkUnitySoundEngine.SetRTPCValue(lateLevelRtpc, _currentLateLevel);
        AkUnitySoundEngine.SetRTPCValue(hfDampingRtpc, _currentHighFreqDamping);
        AkUnitySoundEngine.SetRTPCValue(roomSizeRtpc, _currentRoomSize);
        AkUnitySoundEngine.SetRTPCValue(sendGainRtpc, _currentSendGain);
        AkUnitySoundEngine.SetRTPCValue(externalSendGainRtpc, _currentExternalSendGain);
        AkUnitySoundEngine.SetRTPCValue(roomTypeRtpc, _currentRoomType);
        AkUnitySoundEngine.SetRTPCValue(panBalanceRtpc, _currentPanBalance);
        AkUnitySoundEngine.SetRTPCValue(earlyDiffRtpc, _currentEarlyDiff);
    }

    private void ResetWwiseRtpc()
    {
        _targetDecay = 0f;
        _targetEarlyLevel = -96f;
        _targetLateLevel = -96f;
        _targetHFDamping = -96f;
        _targetSendGain = -96f;
        _targetExternalSendGain = -96f;
        _targetRoomSize = 0f;
        _targetRoomType = -1f;
        _targetPanBalance = 0f;
        _targetEarlyDiff = 0f;

        _prevTargetDecay = _targetDecay;
        _prevTargetEarlyLevel = _targetEarlyLevel;
        _prevTargetLateLevel = _targetLateLevel;
        _prevTargetHFDamping = _targetHFDamping;
        _prevTargetSendGain = _targetSendGain;
        _prevTargetRoomSize = _targetRoomSize;
        _prevTargetRoomType = _targetRoomType;
    }

    private void ApplyInterpolation()
    {
        float speed = lerpSpeed * _transitionMultiplier;
        float reverbSpeed = speed * 5f;

        _currentDecay = Mathf.Lerp(_currentDecay, _targetDecay, Time.deltaTime * reverbSpeed);
        _currentEarlyLevel = Mathf.Lerp(_currentEarlyLevel, _targetEarlyLevel, Time.deltaTime * reverbSpeed);
        _currentLateLevel = Mathf.Lerp(_currentLateLevel, _targetLateLevel, Time.deltaTime * reverbSpeed);
        _currentHighFreqDamping = Mathf.Lerp(_currentHighFreqDamping, _targetHFDamping, Time.deltaTime * reverbSpeed);
        _currentSendGain = Mathf.Lerp(_currentSendGain, _targetSendGain, Time.deltaTime * reverbSpeed);
        _currentExternalSendGain = Mathf.Lerp(_currentExternalSendGain, _targetExternalSendGain, Time.deltaTime * reverbSpeed);
        _currentRoomSize = Mathf.Lerp(_currentRoomSize, _targetRoomSize, Time.deltaTime * reverbSpeed);
        _currentRoomType = Mathf.Lerp(_currentRoomType, _targetRoomType, Time.deltaTime * reverbSpeed);
        _currentPanBalance = Mathf.Lerp(_currentPanBalance, _targetPanBalance, Time.deltaTime * reverbSpeed);
        _currentEarlyDiff = Mathf.Lerp(_currentEarlyDiff, _targetEarlyDiff, Time.deltaTime * reverbSpeed);
    }

    private void DrawDebugRays()
    {
        int count = _directions.Length;
        for (int i = 0; i < count; i++)
        {
            Color color;
            if (i < 6) color = Color.yellow;
            else if (i < 10) color = Color.cyan;
            else if (i < 14) color = Color.green;
            else color = Color.red;

            if (Physics.Raycast(transform.position, _directions[i], out RaycastHit hit, maxRaycastDistance, wallLayer))
            {
                Debug.DrawLine(transform.position, hit.point, color, _effectiveScanInterval);
            }
            else
            {
                Vector3 endPoint = transform.position + _directions[i] * maxRaycastDistance;
                Debug.DrawLine(transform.position, endPoint, new Color(color.r, color.g, color.b, 0.2f), _effectiveScanInterval);
            }
        }
    }

    private void OnDrawGizmos()
    {
        int count = _directions.Length;
        for (int i = 0; i < count; i++)
        {
            if (i < 6) Gizmos.color = Color.yellow;
            else if (i < 10) Gizmos.color = Color.cyan;
            else if (i < 14) Gizmos.color = Color.green;
            else Gizmos.color = Color.red;

            if (Physics.Raycast(transform.position, _directions[i], out RaycastHit hit, maxRaycastDistance, wallLayer))
            {
                Gizmos.DrawLine(transform.position, hit.point);
                Gizmos.DrawWireSphere(hit.point, 0.2f);
            }
            else
            {
                Vector3 endPoint = transform.position + _directions[i] * maxRaycastDistance;
                Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.2f);
                Gizmos.DrawLine(transform.position, endPoint);
            }
        }
    }
}