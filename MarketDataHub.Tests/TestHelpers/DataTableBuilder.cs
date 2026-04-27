using System;
using System.Data;

namespace MarketDataHub.Tests.TestHelpers
{
    public static class DataTableBuilder
    {
        public static DataTable CreateInstrumentTable(
            params (int id, string ric, string ticker, string name, int isSuspended)[] instruments)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("InstrumentId", typeof(int));
            dt.Columns.Add("RIC", typeof(string));
            dt.Columns.Add("Ticker", typeof(string));
            dt.Columns.Add("InstrumentName", typeof(string));
            dt.Columns.Add("IsSuspended", typeof(int));
            dt.Columns.Add("ISIN", typeof(string));
            dt.Columns.Add("SEDOL", typeof(string));
            dt.Columns.Add("Exchange", typeof(string));
            dt.Columns.Add("Currency", typeof(string));

            foreach (var inst in instruments)
            {
                DataRow row = dt.NewRow();
                row["InstrumentId"] = inst.id;
                row["RIC"] = inst.ric;
                row["Ticker"] = inst.ticker;
                row["InstrumentName"] = inst.name;
                row["IsSuspended"] = inst.isSuspended;
                row["ISIN"] = "GB00" + inst.ticker.PadRight(8, '0');
                row["SEDOL"] = inst.ticker.PadRight(7, '0');
                row["Exchange"] = "LSE";
                row["Currency"] = "GBP";
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateTickTable(
            params (int tickId, int instrumentId, string ric, DateTime timestamp,
            decimal bidPrice, decimal askPrice, decimal tradePrice, long tradeVolume,
            string tradeCondition, string feedSource, int sequenceNumber)[] ticks)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("TickId", typeof(int));
            dt.Columns.Add("InstrumentId", typeof(int));
            dt.Columns.Add("RIC", typeof(string));
            dt.Columns.Add("Timestamp", typeof(DateTime));
            dt.Columns.Add("BidPrice", typeof(decimal));
            dt.Columns.Add("AskPrice", typeof(decimal));
            dt.Columns.Add("TradePrice", typeof(decimal));
            dt.Columns.Add("TradeVolume", typeof(long));
            dt.Columns.Add("TradeCondition", typeof(string));
            dt.Columns.Add("FeedSource", typeof(string));
            dt.Columns.Add("SequenceNumber", typeof(int));

            foreach (var tick in ticks)
            {
                DataRow row = dt.NewRow();
                row["TickId"] = tick.tickId;
                row["InstrumentId"] = tick.instrumentId;
                row["RIC"] = tick.ric;
                row["Timestamp"] = tick.timestamp;
                row["BidPrice"] = tick.bidPrice;
                row["AskPrice"] = tick.askPrice;
                row["TradePrice"] = tick.tradePrice;
                row["TradeVolume"] = tick.tradeVolume;
                row["TradeCondition"] = tick.tradeCondition;
                row["FeedSource"] = tick.feedSource;
                row["SequenceNumber"] = tick.sequenceNumber;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateIndexCompositionTable(
            params (decimal price, decimal weight, decimal freeFloat, long sharesInIssue)[] constituents)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("LastPrice", typeof(decimal));
            dt.Columns.Add("Weight", typeof(decimal));
            dt.Columns.Add("FreeFloatFactor", typeof(decimal));
            dt.Columns.Add("SharesInIssue", typeof(long));

            foreach (var c in constituents)
            {
                DataRow row = dt.NewRow();
                row["LastPrice"] = c.price;
                row["Weight"] = c.weight;
                row["FreeFloatFactor"] = c.freeFloat;
                row["SharesInIssue"] = c.sharesInIssue;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateAlertTable(
            params (int alertId, int instrumentId, string alertType, decimal threshold, string email)[] alerts)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("AlertId", typeof(int));
            dt.Columns.Add("InstrumentId", typeof(int));
            dt.Columns.Add("AlertType", typeof(string));
            dt.Columns.Add("ThresholdValue", typeof(decimal));
            dt.Columns.Add("NotifyEmail", typeof(string));

            foreach (var a in alerts)
            {
                DataRow row = dt.NewRow();
                row["AlertId"] = a.alertId;
                row["InstrumentId"] = a.instrumentId;
                row["AlertType"] = a.alertType;
                row["ThresholdValue"] = a.threshold;
                row["NotifyEmail"] = a.email;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateComplianceTable(
            params (string ric, string instrumentName, string exchange, int tickCount,
            DateTime firstTrade, DateTime lastTrade, decimal lowPrice, decimal highPrice,
            long totalVolume)[] rows)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("RIC", typeof(string));
            dt.Columns.Add("InstrumentName", typeof(string));
            dt.Columns.Add("Exchange", typeof(string));
            dt.Columns.Add("TickCount", typeof(int));
            dt.Columns.Add("FirstTrade", typeof(DateTime));
            dt.Columns.Add("LastTrade", typeof(DateTime));
            dt.Columns.Add("LowPrice", typeof(decimal));
            dt.Columns.Add("HighPrice", typeof(decimal));
            dt.Columns.Add("TotalVolume", typeof(long));

            foreach (var r in rows)
            {
                DataRow row = dt.NewRow();
                row["RIC"] = r.ric;
                row["InstrumentName"] = r.instrumentName;
                row["Exchange"] = r.exchange;
                row["TickCount"] = r.tickCount;
                row["FirstTrade"] = r.firstTrade;
                row["LastTrade"] = r.lastTrade;
                row["LowPrice"] = r.lowPrice;
                row["HighPrice"] = r.highPrice;
                row["TotalVolume"] = r.totalVolume;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateEndOfDayTable(
            params (decimal openPrice, decimal highPrice, decimal lowPrice, decimal closePrice,
            decimal adjustedClose, long totalVolume, decimal vwap, int tradeCount,
            decimal turnover)[] rows)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("OpenPrice", typeof(decimal));
            dt.Columns.Add("HighPrice", typeof(decimal));
            dt.Columns.Add("LowPrice", typeof(decimal));
            dt.Columns.Add("ClosePrice", typeof(decimal));
            dt.Columns.Add("AdjustedClose", typeof(decimal));
            dt.Columns.Add("TotalVolume", typeof(long));
            dt.Columns.Add("VWAP", typeof(decimal));
            dt.Columns.Add("TradeCount", typeof(int));
            dt.Columns.Add("Turnover", typeof(decimal));

            foreach (var r in rows)
            {
                DataRow row = dt.NewRow();
                row["OpenPrice"] = r.openPrice;
                row["HighPrice"] = r.highPrice;
                row["LowPrice"] = r.lowPrice;
                row["ClosePrice"] = r.closePrice;
                row["AdjustedClose"] = r.adjustedClose;
                row["TotalVolume"] = r.totalVolume;
                row["VWAP"] = r.vwap;
                row["TradeCount"] = r.tradeCount;
                row["Turnover"] = r.turnover;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateLatestIndexValuesTable(
            params (string indexCode, decimal previousClose)[] rows)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("IndexCode", typeof(string));
            dt.Columns.Add("PreviousClose", typeof(decimal));

            foreach (var r in rows)
            {
                DataRow row = dt.NewRow();
                row["IndexCode"] = r.indexCode;
                row["PreviousClose"] = r.previousClose;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateTickCountByDateTable(
            params (string feedSource, long tickCount, DateTime firstTick, DateTime lastTick)[] rows)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("FeedSource", typeof(string));
            dt.Columns.Add("TickCount", typeof(long));
            dt.Columns.Add("FirstTick", typeof(DateTime));
            dt.Columns.Add("LastTick", typeof(DateTime));

            foreach (var r in rows)
            {
                DataRow row = dt.NewRow();
                row["FeedSource"] = r.feedSource;
                row["TickCount"] = r.tickCount;
                row["FirstTick"] = r.firstTick;
                row["LastTick"] = r.lastTick;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateSuspendedInstrumentsTable(
            params (string ticker, string ric, string suspensionReason)[] rows)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Ticker", typeof(string));
            dt.Columns.Add("RIC", typeof(string));
            dt.Columns.Add("SuspensionReason", typeof(string));

            foreach (var r in rows)
            {
                DataRow row = dt.NewRow();
                row["Ticker"] = r.ticker;
                row["RIC"] = r.ric;
                row["SuspensionReason"] = r.suspensionReason;
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static DataTable CreateEmptyTable()
        {
            return new DataTable();
        }
    }
}
