let _indexUrl = "";
let _activateUrl = "";
let _restoreUrl = "";
let _presetStatusUrl = "";
let _selectedInstance = "";
let _activeGoal = "";
let _statusPollTimer = null;
const STATUS_POLL_MS = 45000;

const PROGRESS_ABBR = {
	CD: "CD",
	ID: "ID",
	HD: "HD",
	Esp: "Esp",
	Energy: "En",
	Comp: "CT",
	Astro: "Ap",
	HST: "HT",
	ST: "ST",
	SY: "SY",
	LightFighter: "LF",
	SmallCargo: "SC",
	LargeCargo: "LC",
	ColonyShip: "CS",
	Recycler: "Rc",
	EspionageProbe: "EP",
	Pathfinder: "Pf"
};

function sortPresets(presets) {
	if (!Array.isArray(presets))
		return presets;

	return [...presets].sort((a, b) => {
		const orderA = Number.isFinite(a.order) ? a.order : Number.MAX_SAFE_INTEGER;
		const orderB = Number.isFinite(b.order) ? b.order : Number.MAX_SAFE_INTEGER;
		if (orderA !== orderB)
			return orderA - orderB;
		return (a.id || "").localeCompare(b.id || "");
	});
}

function Initialize(indexUrl, activateUrl, restoreUrl, presetStatusUrl, selectedInstance, activeGoal) {
	_indexUrl = indexUrl;
	_activateUrl = activateUrl;
	_restoreUrl = restoreUrl;
	_presetStatusUrl = presetStatusUrl;
	_selectedInstance = selectedInstance;
	_activeGoal = activeGoal || "";

	$("#instanceSelect").on("change", function () {
		const file = $(this).val();
		window.location.href = `${_indexUrl}?instanceSettings=${encodeURIComponent(file)}`;
	});

	$("#refreshPresetStatus").on("click", function () {
		refreshPresetStatus();
	});

	refreshPresetStatus();
	_statusPollTimer = setInterval(refreshPresetStatus, STATUS_POLL_MS);
}

function getSelectedInstance() {
	return $("#instanceSelect").val() || _selectedInstance;
}

function formatProgressBadge(progress, completed) {
	if (completed)
		return "Done";

	if (!progress || typeof progress !== "object")
		return "";

	for (const [key, val] of Object.entries(progress)) {
		if (!val || val.current >= val.required)
			continue;

		const parts = key.split(".");
		const name = parts.length > 1 ? parts[1] : key;
		const abbr = PROGRESS_ABBR[name] || PROGRESS_ABBR[key] || name.substring(0, 2).toUpperCase();
		return `${abbr} ${val.current}/${val.required}`;
	}

	return "Done";
}

function applySleepStatus(sleep) {
	const $badge = $("#sleepStatusBadge");
	const $line = $("#sleepStatusLine");

	if (!$badge.length || !$line.length)
		return;

	if (!sleep || !sleep.sleepModeActive) {
		$badge.addClass("d-none").text("").attr("title", "");
		$line.addClass("d-none").text("").removeClass("text-info text-muted");
		return;
	}

	const message = sleep.message || "";
	$line.removeClass("d-none").text(message);

	if (sleep.isSleeping) {
		$badge.removeClass("d-none").addClass("bg-info text-dark").text("Sleeping").attr("title", message);
		$line.removeClass("text-muted").addClass("text-info");
	} else {
		$badge.addClass("d-none").text("").attr("title", "");
		$line.removeClass("text-info").addClass("text-muted");
	}
}

function applyPresetStatus(data) {
	if (!data || !Array.isArray(data.presets))
		return;

	applySleepStatus(data.sleep);

	const offline = !!data.offline;
	$("#presetStatusHint").text(offline ? "Live status unavailable (ogamed offline)." : "");

	const statusById = {};
	for (const preset of data.presets)
		statusById[preset.id] = preset;

	$(".goal-preset").each(function () {
		const $item = $(this);
		const presetId = $item.data("preset-id");
		const status = statusById[presetId];
		const $badge = $item.find(".preset-status-badge");
		const $button = $item.find(".preset-activate-btn");
		const isActive = _activeGoal && presetId === _activeGoal;
		const hasActiveGoal = !!_activeGoal;

		$item.removeClass("completed offline");

		if (offline)
			$item.addClass("offline");

		if (!status) {
			$badge.text("").addClass("d-none");
			return;
		}

		const completed = !!status.completed;
		const badgeText = formatProgressBadge(status.progress, completed);

		if (badgeText) {
			$badge.text(badgeText)
				.removeClass("d-none bg-success bg-secondary")
				.addClass(completed ? "bg-success" : "bg-secondary");
		} else {
			$badge.text("").addClass("d-none");
		}

		if (completed && !isActive)
			$item.addClass("completed");

		if (hasActiveGoal)
			$button.prop("disabled", true);
		else if (completed)
			$button.prop("disabled", true);
		else
			$button.prop("disabled", false);
	});

	organizeCompletedPresets();
}

function comparePresetOrder(a, b) {
	const orderA = parseInt($(a).data("preset-order"), 10);
	const orderB = parseInt($(b).data("preset-order"), 10);
	const safeA = Number.isFinite(orderA) ? orderA : Number.MAX_SAFE_INTEGER;
	const safeB = Number.isFinite(orderB) ? orderB : Number.MAX_SAFE_INTEGER;
	if (safeA !== safeB)
		return safeA - safeB;
	return String($(a).data("preset-id") || "").localeCompare(String($(b).data("preset-id") || ""));
}

function organizeCompletedPresets() {
	const $activeList = $("#activePresetList");
	const $completedList = $("#completedPresetList");
	const $section = $("#completedPresetsSection");

	if (!$activeList.length || !$completedList.length || !$section.length)
		return;

	const $allItems = $activeList.find(".goal-preset").add($completedList.find(".goal-preset"));
	const active = [];
	const completed = [];

	$allItems.each(function () {
		if ($(this).hasClass("completed"))
			completed.push(this);
		else
			active.push(this);
	});

	active.sort(comparePresetOrder);
	completed.sort(comparePresetOrder);

	$activeList.empty().append(active);
	$completedList.empty().append(completed);

	const count = completed.length;
	$("#completedPresetsCount").text(count);
	$section.toggleClass("d-none", count === 0);
}

function refreshPresetStatus() {
	if (!_presetStatusUrl)
		return;

	$.get(_presetStatusUrl, {
		instanceSettings: getSelectedInstance()
	}, function (response) {
		applyPresetStatus(response);
	}).fail(function () {
		applyPresetStatus({ offline: true, presets: [] });
	});
}

function onActivateClick(presetId) {
	if (!confirm(`Activate goal "${presetId}"? Current settings will be snapshotted and patched.`))
		return;

	showLoading();
	$.post(_activateUrl, {
		instanceSettings: getSelectedInstance(),
		presetId: presetId
	}, function (response) {
		hideLoading();
		if (!response.success) {
			alert(response.error);
			return;
		}
		window.location.reload();
	}).fail(function () {
		hideLoading();
		alert("Failed to activate goal.");
	});
}

function onRestoreClick() {
	if (!confirm("Restore baseline settings and clear the active goal?"))
		return;

	showLoading();
	$.post(_restoreUrl, {
		instanceSettings: getSelectedInstance()
	}, function (response) {
		hideLoading();
		if (!response.success) {
			alert(response.error);
			return;
		}
		window.location.reload();
	}).fail(function () {
		hideLoading();
		alert("Failed to restore goal.");
	});
}
