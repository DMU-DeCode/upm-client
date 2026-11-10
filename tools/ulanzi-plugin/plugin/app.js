const METRICS_URL = "http://127.0.0.1:8787/metrics";
const OPTIMIZE_URL = "http://127.0.0.1:8787/optimize";
const SHOW_URL = "http://127.0.0.1:8787/show";

// 등록된 버튼 인스턴스들: context -> actionUUID
const ACTIVE_KEYS = {};

let lastMetrics = {
  online: false,
  cpu: 0,
  ram: 0,
  gpu: 0,
  gpuTemp: 0
};

// Canvas 2D 기반 이미지 생성 (144x144 PNG DataURL)
const canvas = document.createElement("canvas");
canvas.width = 144;
canvas.height = 144;
const ctx = canvas.getContext("2d");

// 정적 PNG 아이콘 캐시 (플러그인 로드 시 미리 로드하여 하드웨어 덱에도 즉시 PNG로 전송)
const STATIC_IMAGES = {};
const STATIC_ICON_MAP = {
  ".app": "../Images/app.png",
  ".cpuicon": "../Images/cpuicon.png",
  ".ramicon": "../Images/ramicon.png",
  ".gpuicon": "../Images/gpuicon.png",
  ".tempicon": "../Images/tempicon.png"
};

function preloadStaticImages() {
  for (const [suffix, relPath] of Object.entries(STATIC_ICON_MAP)) {
    const img = new Image();
    img.onload = () => {
      const c = document.createElement("canvas");
      c.width = 144;
      c.height = 144;
      const g = c.getContext("2d");
      g.drawImage(img, 0, 0, 144, 144);
      STATIC_IMAGES[suffix] = c.toDataURL("image/png");
      for (const [context, uuid] of Object.entries(ACTIVE_KEYS)) {
        if (uuid.endsWith(suffix)) {
          updateKey(context, uuid);
        }
      }
    };
    img.src = relPath;
  }
}
preloadStaticImages();

function drawRoundRect(x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
}

function getHeatColor(percent) {
  if (percent >= 80) return "#EF4444"; // Red
  if (percent >= 60) return "#F59E0B"; // Amber
  return "#10B981"; // Emerald Green
}

function getTempColor(celsius) {
  if (celsius >= 80) return "#EF4444";
  if (celsius >= 70) return "#F59E0B";
  return "#3B82F6";
}

// 캔버스로 동적 수치 그리기
function renderValueImage(label, valStr, unit, heatColor) {
  ctx.clearRect(0, 0, 144, 144);

  // Background
  drawRoundRect(0, 0, 144, 144, 28);
  ctx.fillStyle = "#111827";
  ctx.fill();

  // Subtle border
  drawRoundRect(8, 8, 128, 128, 22);
  ctx.strokeStyle = heatColor;
  ctx.lineWidth = 4;
  ctx.globalAlpha = 0.45;
  ctx.stroke();
  ctx.globalAlpha = 1.0;

  // Label
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = "bold 18px 'Segoe UI', sans-serif";
  ctx.fillStyle = "#9CA3AF";
  ctx.fillText(label, 72, 32);

  // Value
  ctx.font = "900 42px 'Segoe UI', sans-serif";
  ctx.fillStyle = heatColor;
  ctx.fillText(valStr, 72, 76);

  // Unit
  ctx.font = "bold 18px 'Segoe UI', sans-serif";
  ctx.fillStyle = "#9CA3AF";
  ctx.fillText(unit, 72, 116);

  return canvas.toDataURL("image/png");
}

function renderBoostImage(isBoosting, resultText) {
  ctx.clearRect(0, 0, 144, 144);

  drawRoundRect(0, 0, 144, 144, 28);
  ctx.fillStyle = isBoosting ? "#DC2626" : "#2563EB";
  ctx.fill();

  // Draw Lightning / Bolt
  ctx.beginPath();
  ctx.moveTo(76, 20);
  ctx.lineTo(46, 66);
  ctx.lineTo(68, 66);
  ctx.lineTo(60, 102);
  ctx.lineTo(96, 54);
  ctx.lineTo(72, 54);
  ctx.closePath();
  ctx.fillStyle = "#FDE047";
  ctx.fill();

  // Text
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = "900 18px 'Segoe UI', sans-serif";
  ctx.fillStyle = "#FFFFFF";
  ctx.fillText(resultText || "BOOST", 72, 120);

  return canvas.toDataURL("image/png");
}

function extractActionUUID(jsn) {
  if (!jsn) return "";
  if (jsn.uuid && jsn.uuid.split(".").length >= 5) {
    return jsn.uuid;
  }
  if (jsn.context) {
    const decoded = $UD.decodeContext(jsn.context);
    if (decoded && decoded.uuid) return decoded.uuid;
  }
  return jsn.uuid || "";
}

function registerAndRefreshKey(jsn) {
  if (!jsn || !jsn.context) return "";
  const context = jsn.context;
  const actionUUID = extractActionUUID(jsn) || ACTIVE_KEYS[context] || "";
  if (actionUUID) {
    ACTIVE_KEYS[context] = actionUUID;
    updateKey(context, actionUUID);
  }
  return actionUUID;
}

function updateKey(context, actionUUID) {
  if (!context || !actionUUID) return;

  for (const [suffix, dataUrl] of Object.entries(STATIC_IMAGES)) {
    if (actionUUID.endsWith(suffix) && dataUrl) {
      $UD.setBaseDataIcon(context, dataUrl, "");
      return;
    }
  }

  if (actionUUID.endsWith(".boost")) {
    const dataUrl = renderBoostImage(false, "BOOST");
    $UD.setBaseDataIcon(context, dataUrl, "");
    return;
  }

  let dataUrl = null;
  if (actionUUID.endsWith(".cpuval")) {
    const val = lastMetrics.online ? Math.round(lastMetrics.cpu).toString() : "--";
    const color = lastMetrics.online ? getHeatColor(lastMetrics.cpu) : "#3B82F6";
    dataUrl = renderValueImage("CPU", val, "%", color);
  } else if (actionUUID.endsWith(".ramval")) {
    const val = lastMetrics.online ? Math.round(lastMetrics.ram).toString() : "--";
    const color = lastMetrics.online ? getHeatColor(lastMetrics.ram) : "#10B981";
    dataUrl = renderValueImage("RAM", val, "%", color);
  } else if (actionUUID.endsWith(".gpuval")) {
    const val = lastMetrics.online ? Math.round(lastMetrics.gpu).toString() : "--";
    const color = lastMetrics.online ? getHeatColor(lastMetrics.gpu) : "#A855F7";
    dataUrl = renderValueImage("GPU", val, "%", color);
  } else if (actionUUID.endsWith(".tempval")) {
    const val = lastMetrics.online ? Math.round(lastMetrics.gpuTemp).toString() : "--";
    const color = lastMetrics.online ? getTempColor(lastMetrics.gpuTemp) : "#EF4444";
    dataUrl = renderValueImage("GPU TEMP", val, "°C", color);
  }

  if (dataUrl) {
    $UD.setBaseDataIcon(context, dataUrl, "");
  }
}

// 1순위: CORS가 설정된 HTTP API (GET/POST), 2순위: CORS가 없을 때도 POST가 전송되는 no-cors fetch
async function requestUpmApi(url, method = "GET") {
  try {
    const res = await fetch(url, { method, cache: "no-store" });
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    if (method === "POST") {
      // 구버전 서버(CORS 헤더 미포함)라도 simple POST는 서버에서 정상 실행됨!
      await fetch(url, { method: "POST", mode: "no-cors", cache: "no-store" });
      return { status: "success", fallbackNoCors: true };
    }
    throw err;
  }
  return null;
}

// 로컬 metrics.js 스크립트 태그 로드 (CORS 제약 전혀 없음)
function loadLocalMetricsScript() {
  return new Promise((resolve) => {
    const oldScript = document.getElementById("upm-local-metrics");
    if (oldScript) oldScript.remove();

    const s = document.createElement("script");
    s.id = "upm-local-metrics";
    s.src = "./metrics.js?t=" + Date.now();
    s.onload = () => {
      if (window.__UPM_METRICS__ && (Date.now() - (window.__UPM_METRICS__.ts || 0) < 15000)) {
        resolve(window.__UPM_METRICS__);
      } else if (window.__UPM_RAW__ && window.__UPM_RAW__.status === "success") {
        resolve(window.__UPM_RAW__);
      } else {
        resolve(null);
      }
    };
    s.onerror = () => resolve(null);
    document.body.appendChild(s);
  });
}

async function pollMetrics() {
  let updated = false;
  try {
    const data = await requestUpmApi(METRICS_URL, "GET");
    if (data && data.status === "success") {
      lastMetrics = {
        online: true,
        cpu: data.cpu ?? 0,
        ram: data.ram ?? 0,
        gpu: data.gpu ?? 0,
        gpuTemp: data.gpuTemp ?? 0
      };
      updated = true;
    }
  } catch (err) {
    // CORS 등으로 HTTP GET이 막힌 경우 로컬 metrics.js 폴백 사용
  }

  if (!updated) {
    const localData = await loadLocalMetricsScript();
    if (localData) {
      lastMetrics = {
        online: true,
        cpu: localData.cpu ?? 0,
        ram: localData.ram ?? 0,
        gpu: localData.gpu ?? 0,
        gpuTemp: localData.gpuTemp ?? 0
      };
      updated = true;
    }
  }

  if (!updated) {
    lastMetrics.online = false;
  }

  for (const [context, actionUUID] of Object.entries(ACTIVE_KEYS)) {
    if (
      actionUUID.endsWith(".cpuval") ||
      actionUUID.endsWith(".ramval") ||
      actionUUID.endsWith(".gpuval") ||
      actionUUID.endsWith(".tempval")
    ) {
      updateKey(context, actionUUID);
    }
  }
}

// Ulanzi SDK 연결
$UD.connect("com.ulanzi.ulanzistudio.upmmonitor");

$UD.onConnected(() => {
  setInterval(pollMetrics, 1000);
  pollMetrics();
});

$UD.onAdd((jsn) => {
  registerAndRefreshKey(jsn);
  pollMetrics();
});

$UD.onSetActive((jsn) => {
  registerAndRefreshKey(jsn);
  pollMetrics();
});

$UD.onParamFromApp((jsn) => {
  registerAndRefreshKey(jsn);
});

$UD.onParamFromPlugin((jsn) => {
  registerAndRefreshKey(jsn);
});

$UD.onClear((jsn) => {
  if (jsn.param) {
    for (let i = 0; i < jsn.param.length; i++) {
      const context = jsn.param[i].context;
      delete ACTIVE_KEYS[context];
    }
  }
});

async function handleKeyTrigger(jsn) {
  const context = jsn.context;
  const actionUUID = registerAndRefreshKey(jsn);

  if (actionUUID.endsWith(".app")) {
    try {
      await requestUpmApi(SHOW_URL, "POST");
    } catch (e) {
      $UD.showAlert(context);
    }
  } else if (actionUUID.endsWith(".boost")) {
    try {
      const runningData = renderBoostImage(true, "정리중...");
      $UD.setBaseDataIcon(context, runningData, "");

      const json = await requestUpmApi(OPTIMIZE_URL, "POST");
      const label =
        json && typeof json.clearedProcesses === "number"
          ? `${json.clearedProcesses}개 완료`
          : "최적화 완료";
      const doneData = renderBoostImage(false, label);
      $UD.setBaseDataIcon(context, doneData, "");
      setTimeout(() => {
        updateKey(context, actionUUID);
        pollMetrics();
      }, 2500);
    } catch (e) {
      $UD.showAlert(context);
      setTimeout(() => {
        updateKey(context, actionUUID);
      }, 1500);
    }
  }
}

$UD.onRun((jsn) => {
  handleKeyTrigger(jsn);
});
