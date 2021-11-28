using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BTrader.Algo.HFTRules
{

    public abstract class HFTRule
    {
        public static Dictionary<Tuple<double, double>, Dictionary<Tuple<double, double>, HFTRule>> LoadRules(string filePath)
        {
            var result = new Dictionary<Tuple<double, double>, Dictionary<Tuple<double, double>, HFTRule>>();
            var lines = File.ReadAllLines(filePath);
            var columnCriteria = new List<Tuple<double, double>>();
            var columnLineParts = lines[0].Split(',').Skip(1);
            foreach (var column in columnLineParts)
            {
                var columnText = column.Replace("(", "").Replace(")", "");
                var columnParts = columnText.Split(';');
                double leftSide;
                if (!double.TryParse(columnParts[0], out leftSide))
                {
                    leftSide = double.MinValue;
                }
                double rightSide;
                if (!double.TryParse(columnParts[1], out rightSide))
                {
                    rightSide = double.MaxValue;
                }

                columnCriteria.Add(Tuple.Create(leftSide, rightSide));
            }

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                var rowCriteriaText = parts[0].Replace("(", "").Replace(")", "");
                var rowCriteriaTextParts = rowCriteriaText.Split(';');
                double leftSide;
                if (!double.TryParse(rowCriteriaTextParts[0], out leftSide))
                {
                    leftSide = double.MinValue;
                }
                double rightSide;
                if (!double.TryParse(rowCriteriaTextParts[1], out rightSide))
                {
                    rightSide = double.MaxValue;
                }

                var row = new Dictionary<Tuple<double, double>, HFTRule>();
                var rowCriteria = Tuple.Create(leftSide, rightSide);
                for (var i = 0; i < columnCriteria.Count; i++)
                {
                    var cell = parts[i + 1];
                    if (cell == "cc")
                    {
                        row[columnCriteria[i]] = new HFTRuleInventoryControl();
                    }
                    else if (cell == "pc")
                    {
                        row[columnCriteria[i]] = new HFTRulePartialInventoryControl();
                    }
                    else if (cell.StartsWith("mm"))
                    {
                        var cellParts = cell.Substring(2).Replace("(", "").Replace(")", "").Split(';');
                        var leftDistance = int.Parse(cellParts[0]);
                        var rightDistance = int.Parse(cellParts[1]);
                        row[columnCriteria[i]] = new HFTRuleMarketMaking(leftDistance, rightDistance);
                    }
                    else
                    {
                        throw new ApplicationException($"{cell} HFT rule not supported");
                    }
                }

                result.Add(rowCriteria, row);

            }

            return result;
        }
    }

}
