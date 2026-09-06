window.optometriaClinicalSignature = (() => {
    const pads = new Map();

    function getPoint(event, canvas) {
        const rect = canvas.getBoundingClientRect();
        const source = event.touches && event.touches.length > 0 ? event.touches[0] : event;
        return {
            x: source.clientX - rect.left,
            y: source.clientY - rect.top
        };
    }

    function resizeCanvas(canvas) {
        const ratio = Math.max(window.devicePixelRatio || 1, 1);
        const rect = canvas.getBoundingClientRect();
        const width = Math.max(Math.floor(rect.width) || canvas.clientWidth || 360, 200);
        const height = Math.max(Math.floor(rect.height) || canvas.clientHeight || 120, 90);
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        const context = canvas.getContext("2d");
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.lineWidth = 2.2;
        context.lineCap = "round";
        context.lineJoin = "round";
        context.strokeStyle = "#1a1512";
        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, width, height);
        return context;
    }

    function drawImageOnCanvas(canvas, dataUrl) {
        if (!dataUrl || dataUrl.length < 50) {
            return;
        }

        let fullDataUrl = dataUrl;
        if (!fullDataUrl.startsWith("data:image")) {
            fullDataUrl = "data:image/png;base64," + fullDataUrl;
        }

        const context = resizeCanvas(canvas);
        const image = new Image();
        image.onload = () => {
            const w = canvas.clientWidth || Math.floor(canvas.width / (window.devicePixelRatio || 1)) || 360;
            const h = canvas.clientHeight || Math.floor(canvas.height / (window.devicePixelRatio || 1)) || 120;
            context.drawImage(image, 0, 0, w, h);
            const pad = pads.get(canvas.id);
            if (pad) {
                pad.hasInk = true;
            }
        };
        image.src = fullDataUrl;
    }

    function init(canvasId, initialDataUrl) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return false;
        }

        const context = resizeCanvas(canvas);
        let drawing = false;
        const pad = {
            canvas: canvas,
            hasInk: Boolean(initialDataUrl && initialDataUrl.length > 50)
        };

        const start = (event) => {
            drawing = true;
            const point = getPoint(event, canvas);
            context.beginPath();
            context.moveTo(point.x, point.y);
            if (event.cancelable) event.preventDefault();
        };

        const move = (event) => {
            if (!drawing) return;
            const point = getPoint(event, canvas);
            context.lineTo(point.x, point.y);
            context.stroke();
            pad.hasInk = true;
            if (event.cancelable) event.preventDefault();
        };

        const end = (event) => {
            if (!drawing) return;
            drawing = false;
            context.closePath();
            pad.hasInk = true;
            if (event.cancelable) event.preventDefault();
        };

        canvas.onmousedown = start;
        canvas.onmousemove = move;
        canvas.onmouseup = end;
        canvas.onmouseleave = end;
        canvas.ontouchstart = start;
        canvas.ontouchmove = move;
        canvas.ontouchend = end;
        canvas.ontouchcancel = end;

        pads.set(canvasId, pad);

        if (initialDataUrl && initialDataUrl.length > 50) {
            setTimeout(() => {
                drawImageOnCanvas(canvas, initialDataUrl);
            }, 25);
        }

        return true;
    }

    function clear(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        resizeCanvas(canvas);
        const pad = pads.get(canvasId);
        if (pad) {
            pad.hasInk = false;
        }
    }

    function getDataUrl(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return "";
        const pad = pads.get(canvasId);
        if (!pad || !pad.hasInk) {
            return "";
        }
        return canvas.toDataURL("image/png");
    }

    return {
        init,
        clear,
        getDataUrl
    };
})();
