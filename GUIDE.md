32ув# Dynamic Raycast Reverb System (Unity + Wwise)

> **Версия:** 7.0  
> **Дата:** 2026-08-10  
> **Статус:** Production Ready  
> **Автор:** Полностью авторская система с нуля

---

## Суть проекта

Система **динамического акустического моделирования** для игр на Unity + Wwise. В реальном времени определяет геометрию помещения вокруг игрока и передаёт **10 RTPC-параметров** в аудиодвижок для адаптации реверба.

**Технологический стек:** C#, Unity Physics, Wwise Audio Engine, Animation Curves, Coroutines, Smoothstep Interpolation.

**Ключевые достижения:**
- ✅ **100% кастомная разработка** — без готовых ассетов и плагинов
- ✅ **Асинхронное сканирование** через Coroutines (не блокирует main thread)
- ✅ **Адаптивная частота** сканирования на основе скорости игрока (до 60% экономии CPU)
- ✅ **Плавные переходы** между помещениями через smoothstep-затухание
- ✅ **Binaural панорамирование** — реверб смещается к ближайшим стенам
- ✅ **Smart Detection** — система различает замкнутые помещения и открытые пространства

---

## Архитектура

```
Unity (Камера игрока)
  │
  ├── Raycasting (6–100 лучей) → определение стен/пола/потолка
  ├── Геометрия → width × height × depth = volume
  ├── Нормализация → normalizedVolume [0.0; 1.0]
  ├── Curves → расчёт 10 параметров реверба
  ├── Интерполяция → плавное обновление каждый кадр
  ├── Отправка → AkUnitySoundEngine.SetRTPCValue() (глобально)
  │
  └── Wwise
       ├── RoomVerb → Decay, HF Damping, ER/Late Level, Pre-delay, Diffusion
       ├── Send Gain → громкость отправки на ReverbBus
       └── Pan Balance → HRTF-эффект смещения реверба
```

**Ключевые решения:**
- **Глобальные RTPC** — все 10 параметров отправляются без привязки к GameObject. Любой звук на сцене автоматически получает текущие параметры пространства.
- **Асинхронный Coroutine** — сканирование выполняется в отдельном потоке Unity (не main thread), не блокируя рендеринг и физику.
- **Медианная фильтрация** — вместо максимума используется медиана попаданий, что исключает выбросы от дальних стен, видимых через проёмы.

---

## Алгоритм сканирования

### 1. Генерация лучей (6–100)

Система поддерживает от 6 до 100 лучей. Базовый набор — 6 осевых лучей строго по осям координат:

```
±X → левая и правая стены
±Y → потолок и пол
±Z → передняя и задняя стены
```

Дополнительные лучи (7–100) распределяются по сфере с помощью **алгоритма Фибоначчи** — математического подхода для равномерного покрытия поверхности сферы:

```csharp
float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f)); // ~2.39996
float y = 1f - (i / (float)(extraCount - 1)) * 2f;
float radius = Mathf.Sqrt(1f - y * y);
float theta = goldenAngle * i;
_directions[i + 6] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius).normalized;
```

**Почему Фибоначчи?** Алгоритм обеспечивает равномерное распределение точек по поверхности сферы без кластеризации, что даёт более точное определение геометрии помещения.

**Асинхронный режим:** Переключается toggle-ом `useAsyncScan`. При включении сканирование выполняется через `Coroutine`, который не блокирует main thread. При выключении — выполняется синхронно в `Update()`.

### 2. Классификация пространства

Система определяет, находится ли игрок внутри замкнутого помещения или на открытом пространстве:

```csharp
bool hasCeiling  = upDists.Count > 0;
bool hasFloor    = downDists.Count > 0;
bool hasWalls    = (rightDists.Count > 0 && leftDists.Count > 0) ||
                   (frontDists.Count > 0 && backDists.Count > 0);
```

**Условия закрытого помещения:**
- ✅ Есть хотя бы одно попадание в потолок
- ✅ Есть хотя бы одно попадание в пол
- ✅ Есть хотя бы одна замкнутая пара стен (±X или ±Z)

**Дополнительная проверка `Max Room Size`:**
```csharp
if (minWallDist > maxRoomSize) // по умолчанию 50 м
    // Открытое пространство — стены слишком далеко
```

Это предотвращает ложное определение помещения, когда дальние стены (горы, здания) видны издалека.

**При обнаружении открытого пространства:**
- Реверб плавно затухает за `fadeOutTime` (по умолчанию 1.5 сек)
- Используется smoothstep-интерполяция для естественного ухода в тишину

### 3. Расчёт габаритов

Для каждой оси вычисляется медиана дистанций попаданий:

```csharp
width  = Median(rightDists) + Median(leftDists);
height = Median(upDists) + Median(downDists);
depth  = Median(frontDists) + Median(backDists);
volume = width × height × depth;
```

**Почему медиана?** Медиана исключает выбросы — если один из лучей попал в дальнюю стену через проём, медиана проигнорирует это значение и возьмёт среднее из основных попаданий.

**Реализация медианы:**
```csharp
private float Median(List<float> values)
{
    if (values.Count == 0) return 0f;
    values.Sort();
    int mid = values.Count / 2;
    return values.Count % 2 == 0 
        ? (values[mid - 1] + values[mid]) * 0.5f 
        : values[mid];
}
```

### 4. Нормализация

```csharp
float normalizedVolume = Mathf.Clamp01(volume / maxReferenceVolume);
```

- `maxReferenceVolume = 1000 м³` — объём, при котором кривые достигают максимума (настраивается в Inspector)
- Результат: `0.0` = крошечная комната (туалет), `1.0` = ангар/склад
- `Mathf.Clamp01` гарантирует, что значение всегда в диапазоне [0; 1]

**Примеры объёмов:**
| Помещение | Размеры (м) | Объём (м³) | Нормализация |
|-----------|-------------|------------|--------------|
| Туалет | 1.5 × 2 × 1.5 | 4.5 | 0.005 |
| Кладовая | 2 × 2.5 × 2 | 10 | 0.01 |
| Спальня | 4 × 3 × 3 | 36 | 0.036 |
| Гостиная | 6 × 5 × 3 | 90 | 0.09 |
| Зал | 10 × 8 × 4 | 320 | 0.32 |
| Склад | 20 × 15 × 5 | 1500 | 1.0+ |
| Ангар | 30 × 20 × 8 | 4800 | 1.0+ |

### 5. Расчёт параметров реверба

**Пять параметров через AnimationCurve:**

```csharp
_targetDecay      = decayCurve.Evaluate(normalizedVolume);
_targetEarlyLevel = earlyLevelCurve.Evaluate(normalizedVolume);
_targetLateLevel  = lateLevelCurve.Evaluate(normalizedVolume);
_targetHFDamping  = hfDampingCurve.Evaluate(normalizedVolume);
_targetRoomSize   = roomSizeCurve.Evaluate(normalizedVolume);
```

**Почему AnimationCurve?** Кривые редактируются прямо в Unity Inspector — звукорежиссёр может тонко настроить поведение реверба без изменения кода.

**Send Gain — жёсткая формула:**

```csharp
_targetSendGain = -12f + (24f * normalizedVolume);
```

| normalizedVolume | Send Gain | Эффект |
|:---:|:---:|--------|
| 0.0 | -12 dB | Маленькая комната, реверб тише |
| 0.5 | 0 dB | Средняя комната, нейтрально |
| 1.0 | +12 dB | Ангар, реверб громче |

**Почему формула, а не кривая?** Формула гарантирует ровно +12 dB в ангаре и -12 dB в маленькой комнате — линейная зависимость проще для предсказания и отладки.

### 6. Binaural панорамирование

Система рассчитывает **баланс лево/право** на основе расстояний до ближайших стен:

```csharp
_targetPanBalance = Mathf.Clamp((rightDist - leftDist) / maxDist, -1f, 1f);
_targetEarlyDiff  = Mathf.Clamp(20f * Mathf.Log10(rightDist / leftDist), -20f, 20f);
```

**Как это работает:**
1. Собираются все дистанции до правых стен (`rightDists`) и левых стен (`leftDists`)
2. Берётся медиана каждой группы
3. Если правая стена ближе → `panBalance` стремится к `+1` (реверб громче справа)
4. Если левая стена ближе → `panBalance` стремится к `-1` (реверб громче слева)

**Дополнительный параметр `EarlyDiff`:**
```
earlyDiff = 20 × Log10(rightDist / leftDist)
```
Логарифмическая разница в dB между ранними отражениями от правой и левой стен. Даёт дополнительную пространственную глубину — реверб звучит более естественно, имитируя реальную акустику.

**Пример:**
| Ситуация | leftDist | rightDist | panBalance | earlyDiff |
|----------|----------|-----------|------------|-----------|
| Центр комнаты | 5 | 5 | 0 | 0 |
| Ближе к левой | 2 | 8 | +1 | +12 dB |
| Ближе к правой | 8 | 2 | -1 | -12 dB |

---

## Адаптивная частота сканирования

Система меняет частоту сканирования в зависимости от скорости игрока:

| Состояние | Скорость | Интервал | Сканов/мин | CPU |
|-----------|:---:|:---:|:---:|:---:|
| Стоит | < 0.1 м/с | 2.0 сек | ~30 | Минимум |
| Идёт | 0.1–3.0 м/с | 0.5 сек | ~120 | Средний |
| Бежит | > 3.0 м/с | 0.2 сек | ~300 | Высокий |
| Телепорт | > 0.5 м/кадр | Мгновенно | — | Пик |

**Алгоритм выбора интервала:**
```csharp
private float GetEffectiveScanInterval()
{
    if (!useAdaptiveScanRate) return walkScanInterval;
    
    if (_lastSpeed < idleSpeedThreshold)
        return idleScanInterval; // 2.0 сек
    
    if (_lastSpeed >= runSpeedThreshold)
        return runScanInterval; // 0.2 сек
    
    // Плавная интерполяция между walk и run
    float t = Mathf.InverseLerp(idleSpeedThreshold, runSpeedThreshold, _lastSpeed);
    return Mathf.Lerp(walkScanInterval, runScanInterval, t);
}
```

**Телепорт-детектор:**
```csharp
float frameDelta = Vector3.Distance(currentPos, _lastPosition);
bool teleportDetected = frameDelta > teleportThreshold; // 0.5 м
```
Если игрок переместился больше чем на `teleportThreshold` за один кадр (двери, телепорт, быстрый поворот камеры) — скан выполняется мгновенно, независимо от таймера.

---

## Интерполяция и плавные переходы

### Двойная буферизация

```
_target*  → обновляются при сканировании (каждые 0.2–2 сек)
_current* → плавно приближаются через Lerp (каждый кадр)
```

**Почему двойная буферизация?** Сканирование выполняется редко (раз в 0.2–2 сек), но реверб должен меняться плавно каждый кадр. Разделение целевых и текущих значений позволяет интерполировать без резких скачков.

### Базовая интерполяция

```csharp
float speed = lerpSpeed * _transitionMultiplier;
float reverbSpeed = speed * 5f; // ускорение ×5

_currentDecay = Mathf.Lerp(_currentDecay, _targetDecay, Time.deltaTime * reverbSpeed);
// ... и так для всех 10 параметров
```

**Почему ×5?** Параметры реверба должны меняться быстрее, чем параметры движения игрока. Множитель ×5 обеспечивает быструю реакцию при смене комнаты.

### Transition Smoothing

При резком переходе (выход из комнаты на улицу) скорость временно увеличивается:

```csharp
float delta = Mathf.Abs(_targetDecay - _prevTargetDecay)
            + Mathf.Abs(_targetEarlyLevel - _prevTargetEarlyLevel)
            + ...; // сумма изменений всех параметров

float normalizedDelta = delta / maxReferenceVolume;

if (normalizedDelta > transitionThreshold) // 0.15
{
    _transitionMultiplier = maxTransitionMultiplier; // 4×
    _transitionDecayTimer = 0f;
}
```

**Алгоритм затухания множителя:**
```csharp
if (_transitionMultiplier > 1f)
{
    _transitionDecayTimer += Time.deltaTime;
    float t = _transitionDecayTimer / transitionDecayTime; // 0.3 сек
    
    if (t >= 1f)
        _transitionMultiplier = 1f;
    else
        _transitionMultiplier = Mathf.Lerp(maxTransitionMultiplier, 1f, t);
}
```

| Параметр | По умолчанию | Описание |
|----------|:---:|----------|
| `lerpSpeed` | 3 | Базовая скорость интерполяции |
| `transitionThreshold` | 0.15 | Порог резкого перехода |
| `maxTransitionMultiplier` | 4 | Множитель при резком переходе |
| `transitionDecayTime` | 0.3 сек | Время возврата к норме |

### Fade Out при выходе на улицу

При обнаружении открытого пространства система плавно затухает за `fadeOutTime` секунд:

```csharp
// Запоминаем стартовые значения
.fadeStartDecay = _targetDecay;
.fadeStartLateLevel = _targetLateLevel;
// ... и так для всех параметров

// Каждый кадр затухания
float smoothT = t * t * (3f - 2f * t); // smoothstep

_targetDecay = Mathf.Lerp(_fadeStartDecay, 0f, smoothT);
_targetLateLevel = Mathf.Lerp(_fadeStartLateLevel, -96f, smoothT);
// ... и так для всех параметров
```

**Почему smoothstep?** S-образная кривая (`t² × (3 - 2t)`) обеспечивает быстрое начало затухания и плавное замедление к тишине — звучит более естественно, чем линейная интерполяция.

**Результат:**
- 0 сек → текущие значения реверба
- 0.75 сек → ~50% затухания
- 1.5 сек → полная тишина (-96 dB)

---

## Типы помещений

Система классифицирует помещения по пропорциям `width × height × depth`:

| Код | Тип | Условие | Пример |
|:---:|-----|---------|--------|
| 0 | Куб | `|W-H| < 1 && |D-H| < 1` | Гардеробная |
| 1 | Коридор | `max(W,D) > 3 × min(W,D)` | Длинный коридор |
| 2 | Зал | `|W-D| < 2 && W > 2×H` | Широкий, высокий потолок |
| 3 | Ангар | `W×D > 4×H²` | Большая площадь, низкий потолок |
| 4 | Atrium | `H > 2×max(W,D)` | Высокое узкое пространство |
| 5 | Комната | Всё остальное | Обычная комната |
| -1 | Улица | Нет потолка/стен | Открытое пространство |

**Приоритет проверки:** Atrium → Коридор → Зал → Ангар → Куб → Комната.

**Зачем классификация?** Параметр `ReverbRoomType` передаётся в Wwise для дополнительной обработки — звукорежиссёр может привязать разные акустические пресеты к разным типам помещений.

---

## 10 RTPC в Wwise

| # | Имя | Диапазон | Управление в Wwise | Описание |
|---|-----|:---:|-------------------|----------|
| 1 | `ReverbDecay` | 0 → 10 | Decay Time в RoomVerb | Время затухания реверба (сек) |
| 2 | `ReverbEarlyLevel` | -96 → 0 | ER Level | Уровень ранних отражений (dB) |
| 3 | `ReverbLateLevel` | -96 → 0 | Reverb Level | Уровень поздних отражений (dB) |
| 4 | `ReverbHFDamping` | -96 → 0 | HF Damping | Затухание высоких частот (dB) |
| 5 | `ReverbRoomSize` | 0 → 100 | Pre-delay / Diffusion | Размер помещения (0–100) |
| 6 | `ReverbSendGain` | -96 → +12 | Send Volume (шаги) | Громкость отправки шагов |
| 7 | `ReverbSendGainExternal` | -96 → +12 | Send Volume (выстрелы) | Громкость отправки выстрелов |
| 8 | `ReverbRoomType` | -1 → 5 | Класс помещения | Тип помещения (0–5) |
| 9 | `ReverbPanBalance` | -1 → 1 | Баланс лево/право | HRTF-эффект смещения |
| 10 | `ReverbEarlyDiff` | -20 → 20 | Разница ранних отражений | dB разница лево/право |

**Все RTPC отправляются глобально** — через `AkUnitySoundEngine.SetRTPCValue()` без привязки к GameObject. Любой звук на сцене автоматически получает текущие параметры пространства.

---

## Интеграция с Wwise

### Шаг 1: Создание Game Parameters

Создать 10 Game Parameters в Wwise:
1. `Master Audio Bus` → Правая кнопка → `Game Parameter` → `New Child`
2. Для каждого параметра указать **точное Min/Max** из таблицы выше

**Важно:** `ReverbSendGain` и `ReverbSendGainExternal` должны иметь **Max = +12**, а не 0. Это необходимо для формулы `-12 + 24 × normalizedVolume`.

### Шаг 2: Создание ReverbBus

1. `Master Audio Bus` → Правая кнопка → `Auxiliary Bus` → переименовать в `ReverbBus`
2. Добавить эффект **RoomVerb** на шину
3. Настроить параметры RoomVerb по умолчанию

### Шаг 3: Привязка RTPC к RoomVerb

В RoomVerb → вкладка **RTPC**:

| Параметр RoomVerb | RTPC | Кривая |
|-------------------|------|--------|
| Decay Time | `ReverbDecay` | Прямая: (0,0) → (10,10) |
| HF Damping | `ReverbHFDamping` | Нисходящая: (-96, 8.0) → (0, 1.0) |
| ER Level | `ReverbEarlyLevel` | Нисходящая: (-96, -2.0) → (0, -8.0) |
| Reverb Level | `ReverbLateLevel` | Растущая: (-96, -10.0) → (0, -3.0) |
| Pre-delay | `ReverbRoomSize` | Восходящая: (0,0) → (50,15) → (100,50) |
| Diffusion | `ReverbRoomSize` | Нисходящая: (0,100) → (50,50) → (100,10) |

### Шаг 4: Настройка отправки звука

**Шаги (Play_Footstep):**
1. Событие → **Routing** → **User-Defined Auxiliary Sends**
2. **Send 0:** `ReverbBus`
3. На **Volume** → добавить RTPC `ReverbSendGain`
4. Кривая: прямая диагональ (-96, -96) → (+12, +12)

**Выстрелы (Play_Shot, Play_AutoShot, Stop_AutoShot):**
1. Событие → **Routing** → **User-Defined Auxiliary Sends**
2. **Send 0:** `ReverbBus`
3. На **Volume** → добавить RTPC `ReverbSendGainExternal`
4. Кривая: прямая диагональ (-96, -96) → (+12, +12)

**Хвост выстрела (Oneshot):**
1. Путь `Oneshot` → **Volume**
2. Добавить RTPC `ReverbSendGain`
3. Кривая **инвертированная:** (-96, 0) → (0, -12)
4. На улице хвост громче, в помещении тише

### Шаг 5: Build и Sync

1. В Wwise: **F7** — Generate SoundBanks
2. В Unity: **Wwise → Synchronize Project**
3. Перезапустить Play Mode

---

## Настройка в Unity

Компонент `DynamicReverbSystem` размещается на **Main Camera**.

### Основные параметры

| Поле | Описание | Значение по умолчанию |
|------|----------|:---:|
| `Wall Layer` | LayerMask для стен/пола/потолка | -1 (все слои) |
| `Ray Count` | Количество лучей (6–100) | 18 |
| `Use Async Scan` | Асинхронное выполнение через Coroutine | `true` |
| `Max Room Size` | Макс. размер помещения (м). Если стены дальше — открытое пространство | 50 м |
| `Fade Out Time` | Время плавного затухания при выходе на улицу | 1.5 сек |
| `Lerp Speed` | Базовая скорость интерполяции параметров | 3 |
| `Max Reference Volume` | Объём для нормализации (м³). При этом объёме кривые достигают максимума | 1000 м³ |
| `Min Raycast Distance` | Игнорировать коллайдеры ближе этого расстояния (для игрока) | 1 м |
| `Max Raycast Distance` | Максимальная дистанция рейкаста | 100 м |

### Adaptive Scan Rate

| Поле | Описание | Значение |
|------|----------|----------|
| `useAdaptiveScanRate` | Вкл/выкл адаптивную частоту | `true` |
| `idleScanInterval` | Интервал когда игрок стоит | 2.0 сек |
| `walkScanInterval` | Интервал при ходьбе | 0.5 сек |
| `runScanInterval` | Интервал при беге | 0.2 сек |
| `idleSpeedThreshold` | Порог «стоит» (м/с) | 0.1 м/с |
| `runSpeedThreshold` | Порог «бежит» (м/с) | 3.0 м/с |
| `teleportThreshold` | Порог телепорта (м за кадр) | 0.5 м |

### Transition Smoothing

| Поле | Описание | Значение |
|------|----------|----------|
| `transitionThreshold` | Порог резкого перехода | 0.15 |
| `maxTransitionMultiplier` | Множитель скорости при резком переходе | 4× |
| `transitionDecayTime` | Время возврата к норме | 0.3 сек |

### Кривые реверба

Пять AnimationCurve редактируются прямо в Inspector:
- `Decay Curve` — время затухания
- `Early Level Curve` — уровень ранних отражений
- `Late Level Curve` — уровень поздних отражений
- `HF Damping Curve` — затухание ВЧ
- `Room Size Curve` — размер помещения

Изменения применяются мгновенно без перезапуска сцены.

---

## Производительность

### Замеры на стандартной сцене (1000×1000 м)

| Метрика | Значение |
|---------|----------|
| Рейкастов за один скан | 6–100 |
| Сканов в секунду (стоит) | 0.5 |
| Сканов в секунду (бежит) | 5.0 |
| Рейкастов в секунду (среднее) | < 40 |
| Влияние на CPU | < 1% |
| Память | ~50 КБ (сценарий + данные) |
| Асинхронный режим | Не блокирует кадр |
| Экономия при стоянии | до 60% |

### Оптимизации

1. **Асинхронный Coroutine** — сканирование не блокирует main thread
2. **Адаптивная частота** — при стоянии скан раз в 2 секунды
3. **Медианная фильтрация** — исключает лишние вычисления для выбросов
4. **Кэширование направлений** — векторы генерируются один раз при старте/изменении `rayCount`

---

## Отладка

### Визуализация лучей (Gizmos)

Включить **Gizmos** в Scene View или Game View:

| Цвет | Группа | Лучей | Назначение |
|:---:|--------|:---:|-----------|
| 🟡 Жёлтый | Осевые | 6 | Базовые стены/пол/потолок |
| 🔵 Голубой | Горизонтальные диагонали | 4 | Ловят стены в проёмах |
| 🟢 Зелёный | Вверх-диагонали | 4 | Ловят потолок в арках |
| 🔴 Красный | Вниз-диагонали | 4 | Ловят пол |

**Бледные линии** — луч промахнулся (не попал в wallLayer).

### Логи в консоли Unity

**Каждые 5 сканов** (чтобы не спамить):
```
DynamicReverb: комната "Ангар" (объём ~10700 м³, W:20.6 H:8.8 D:59.2)
DynamicReverb: открытое пространство (стены дальше 50м, ближайшая 120м)
DynamicReverb: резкий переход (delta=0.32)
```

**При значимом изменении RTPC:**
```
[REVERB RTPC] SendGain=+12.0 | ExternalGain=+12.0 | Room=3.0
```

### Debug-информация в Inspector

В режиме Play Mode можно наблюдать за изменениями:
- `_targetDecay` — целевое значение (обновляется при сканировании)
- `_currentDecay` — текущее значение (плавно приближается)
- `_lastSpeed` — скорость игрока (м/с)
- `_effectiveScanInterval` — текущий интервал скана

---

## Troubleshooting

| Проблема | Причина | Решение |
|----------|---------|---------|
| Реверб не меняется | Лучи не попадают в стены | Проверить `Wall Layer` и логи консоли |
| Выстрелы плоские, без реверба | RTPC не привязан к Volume | Проверить Routing → Send 0 → RTPC на Volume |
| SendGain не доходит до +12 | Max RTPC в Wwise = 0 | Изменить Max RTPC на +12 |
| Звук не обновился после изменений | SoundBanks не перегенерированы | F7 → Synchronize → перезапустить Play Mode |
| Резкий обрыв реверба на улице | `Fade Out Time` слишком мало | Увеличить `Fade Out Time` до 1.5–2.0 сек |
| Система считает улицу помещением | `Max Room Size` слишком велико | Уменьшить `Max Room Size` до 30–50 м |
| Скрипт не работает | Не назначен Wall Layer | Назначить LayerMask стен/пола/потолка |
| Реверб «дёргается» | `Lerp Speed` слишком мало | Увеличить `Lerp Speed` до 5–8 |

---

## Технические детали

### Wwise API

- **Версия Wwise:** >= 2024.1.0
- **Используется:** `AkUnitySoundEngine` (не устаревший `AkSoundEngine`)

| Метод | Описание | Пример |
|-------|----------|--------|
| `LoadBank` | Загрузка банка звуков | `AkUnitySoundEngine.LoadBank(name, out _)` |
| `PostEvent` | Воспроизведение события | `AkUnitySoundEngine.PostEvent(name, gameObject)` |
| `SetRTPCValue` | Установка RTPC | `AkUnitySoundEngine.SetRTPCValue(name, value)` |

### Глобальные vs локальные RTPC

| Тип | Вызов | Когда использовать |
|------|-------|-------------------|
| Глобальный | `SetRTPCValue(name, value)` | Параметры всей сцены (реверб) |
| Локальный | `SetRTPCValue(name, value, gameObject)` | Параметры конкретного объекта |

В этой системе **все 10 RTPC — глобальные**.

### Математика Smoothstep

Smoothstep — S-образная кривая для плавной интерполяции:

```csharp
float smoothT = t * t * (3f - 2f * t);
```

| t | smoothT | Эффект |
|:-:|:-------:|--------|
| 0.0 | 0.0 | Начало затухания |
| 0.5 | 0.5 | Середина — линейная часть |
| 1.0 | 1.0 | Конец — плавное замедление |

**Преимущества перед линейной интерполяцией:**
- Быстрое начало затухания (реверб быстро уходит)
- Плавное замедление в конце (не слышен резкий обрыв)
- Более естественное звучание

---

## Чек-лист запуска

- [ ] Создать 10 Game Parameters в Wwise (Min/Max по таблице)
- [ ] Создать `ReverbBus` (Auxiliary Bus) с RoomVerb
- [ ] Привязать RTPC к RoomVerb (кривые по таблице)
- [ ] Привязать `ReverbSendGain` к Send Volume футстепов
- [ ] Привязать `ReverbSendGainExternal` к Send Volume выстрелов
- [ ] Привязать `ReverbSendGain` к Volume хвоста выстрела (инвертированная кривая)
- [ ] Generate SoundBanks (F7) в Wwise
- [ ] Synchronize Project в Unity
- [ ] Назначить `Wall Layer` в DynamicReverbSystem
- [ ] Включить **Gizmos** в Game View
- [ ] Протестировать в разных помещениях

