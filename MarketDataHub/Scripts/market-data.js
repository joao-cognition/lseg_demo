// MarketDataHub - Client-side JavaScript
// Last updated: Feb 2019 by Stuart M.
// jQuery not used - vanilla JS only (per 2018 security audit recommendation)

var MarketDataHub = {

    // Refresh feed status indicator via AJAX
    refreshFeedStatus: function() {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', '/Dashboard/FeedStatus', true);
        xhr.onreadystatechange = function() {
            if (xhr.readyState === 4 && xhr.status === 200) {
                try {
                    var data = JSON.parse(xhr.responseText);
                    var banner = document.querySelector('.feed-status');
                    if (banner) {
                        if (data.connected) {
                            banner.className = 'feed-status feed-connected';
                        } else {
                            banner.className = 'feed-status feed-disconnected';
                        }
                        // Update tick count
                        banner.innerHTML = '<span class="feed-indicator"></span>' +
                            '<strong>FIX Feed:</strong> ' + (data.connected ? 'Connected' : 'DISCONNECTED') +
                            '&nbsp;|&nbsp;<strong>Ticks Today:</strong> ' + data.ticksToday.toLocaleString() +
                            '&nbsp;|&nbsp;<strong>Last Tick:</strong> ' + data.lastTick;
                    }
                } catch (e) {
                    console.log('Feed status parse error: ' + e);
                }
            }
        };
        xhr.send();
    },

    // Format large numbers with commas
    formatNumber: function(num) {
        return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    },

    // Color-code price changes
    colorizeChanges: function() {
        var cells = document.querySelectorAll('.change-cell');
        for (var i = 0; i < cells.length; i++) {
            var value = parseFloat(cells[i].textContent);
            if (value > 0) {
                cells[i].className += ' positive';
            } else if (value < 0) {
                cells[i].className += ' negative';
            }
        }
    },

    // Simple table sorting (click column header to sort)
    initTableSort: function() {
        var headers = document.querySelectorAll('.data-table th');
        for (var i = 0; i < headers.length; i++) {
            headers[i].style.cursor = 'pointer';
            headers[i].addEventListener('click', function() {
                var table = this.closest('table');
                var index = Array.prototype.indexOf.call(this.parentNode.children, this);
                var rows = Array.prototype.slice.call(table.querySelectorAll('tbody tr'));
                var ascending = this.getAttribute('data-sort') !== 'asc';
                
                rows.sort(function(a, b) {
                    var aVal = a.children[index].textContent.trim();
                    var bVal = b.children[index].textContent.trim();
                    
                    // Try numeric comparison first
                    var aNum = parseFloat(aVal.replace(/[,%]/g, ''));
                    var bNum = parseFloat(bVal.replace(/[,%]/g, ''));
                    
                    if (!isNaN(aNum) && !isNaN(bNum)) {
                        return ascending ? aNum - bNum : bNum - aNum;
                    }
                    return ascending ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
                });
                
                var tbody = table.querySelector('tbody');
                for (var j = 0; j < rows.length; j++) {
                    tbody.appendChild(rows[j]);
                }
                
                this.setAttribute('data-sort', ascending ? 'asc' : 'desc');
            });
        }
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', function() {
    MarketDataHub.colorizeChanges();
    MarketDataHub.initTableSort();
});
