package com.winlock.parent.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.snapping.rememberSnapFlingBehavior
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import kotlin.math.abs

private val ItemHeight = 44.dp

// Repeats of the value range on either side of the starting position, so scrolling feels
// like an endless wheel (00 follows 23, not a hard stop) without actually being infinite —
// a real time picker interaction never gets anywhere near this many revolutions.
private const val WheelRepeats = 400

/**
 * A wheel-style hour:minute picker, built from scratch rather than the system
 * android.app.TimePickerDialog — that dialog's actual look (spinner vs. clock-face dial)
 * depends entirely on the device/OEM/theme and isn't something this app can control, which is
 * exactly the mismatch this replaces. Seconds don't exist here at all — only whole minutes
 * ever leave this dialog, matching the once-a-minute granularity everything downstream uses.
 */
@Composable
fun WheelTimePickerDialog(
    initialHour: Int,
    initialMinute: Int,
    onDismiss: () -> Unit,
    onConfirm: (hour: Int, minute: Int) -> Unit,
) {
    var hour by remember { mutableStateOf(initialHour.coerceIn(0, 23)) }
    var minute by remember { mutableStateOf(initialMinute.coerceIn(0, 59)) }

    Dialog(onDismissRequest = onDismiss) {
        Surface(shape = RoundedCornerShape(28.dp), color = Color.White, tonalElevation = 4.dp) {
            Column(
                modifier = Modifier.padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Text(
                    "ВВЕДИТЕ ВРЕМЯ",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )

                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.padding(top = 14.dp, bottom = 6.dp),
                ) {
                    NumberWheel(range = 0..23, value = hour, onValueChange = { hour = it })
                    Text(
                        ":",
                        fontSize = 28.sp,
                        fontWeight = FontWeight.Medium,
                        modifier = Modifier.padding(horizontal = 6.dp),
                    )
                    NumberWheel(range = 0..59, value = minute, onValueChange = { minute = it })
                }

                Row(
                    horizontalArrangement = Arrangement.End,
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                ) {
                    TextButton(onClick = onDismiss) { Text("Отмена") }
                    TextButton(onClick = { onConfirm(hour, minute) }) { Text("Сохранить") }
                }
            }
        }
    }
}

@Composable
private fun NumberWheel(range: IntRange, value: Int, onValueChange: (Int) -> Unit) {
    val count = range.count()
    val virtualCount = count * WheelRepeats
    val startIndex = remember { virtualCount / 2 - (virtualCount / 2) % count + (value - range.first) }

    // The viewport is exactly 3 rows tall with 1 row of blank padding above/below, so at rest
    // (zero scroll offset within a row) the middle of those 3 visible rows is the selection —
    // hence starting 1 row before the target so it lands there, not at the top.
    val listState = rememberLazyListState(initialFirstVisibleItemIndex = (startIndex - 1).coerceAtLeast(0))
    val flingBehavior = rememberSnapFlingBehavior(listState)

    // Whichever visible row's vertical center is closest to the viewport's center is "the"
    // selected value — true regardless of exactly where a fling settles, since every row is
    // the same height and the middle of 3 equally-spaced visible rows is always one specific
    // relative row once things are at rest.
    val centerIndex by remember {
        derivedStateOf {
            val info = listState.layoutInfo
            val viewportCenter = (info.viewportStartOffset + info.viewportEndOffset) / 2
            info.visibleItemsInfo.minByOrNull { abs((it.offset + it.size / 2) - viewportCenter) }?.index ?: startIndex
        }
    }

    LaunchedEffect(centerIndex) {
        onValueChange(range.first + centerIndex.mod(count))
    }

    Box(
        modifier = Modifier.height(ItemHeight * 3).width(74.dp),
        contentAlignment = Alignment.Center,
    ) {
        LazyColumn(
            state = listState,
            flingBehavior = flingBehavior,
            modifier = Modifier.height(ItemHeight * 3),
            contentPadding = PaddingValues(vertical = ItemHeight),
        ) {
            items(virtualCount) { i ->
                val displayValue = range.first + (i % count)
                val isCenter = i == centerIndex
                Box(
                    modifier = Modifier.height(ItemHeight).fillMaxWidth(),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        "%02d".format(displayValue),
                        fontSize = if (isCenter) 28.sp else 18.sp,
                        fontWeight = if (isCenter) FontWeight.Medium else FontWeight.Normal,
                        color = if (isCenter) Color(0xFF1C1B1F) else Color(0xFFCAC4D0),
                        modifier = if (isCenter) {
                            Modifier
                                .background(Color(0xFFEADDFF), RoundedCornerShape(8.dp))
                                .padding(horizontal = 18.dp, vertical = 2.dp)
                        } else {
                            Modifier
                        },
                    )
                }
            }
        }
    }
}
